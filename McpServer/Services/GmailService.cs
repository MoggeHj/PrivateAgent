using System.Text;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Gmail.v1;
using Google.Apis.Gmail.v1.Data;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using MimeKit;

namespace McpServer.Services;

/// <summary>
/// Wrapper around the Google Gmail API <see cref="Google.Apis.Gmail.v1.GmailService"/> providing helper operations.
/// </summary>
public sealed class GmailService : IGmailService, IDisposable
{
    private readonly Google.Apis.Gmail.v1.GmailService _service;
    private bool _disposed;

    public GmailService(Google.Apis.Gmail.v1.GmailService service)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
    }

    private static readonly string[] Scopes = [Google.Apis.Gmail.v1.GmailService.Scope.GmailReadonly];

    public static async Task<GmailService> CreateAsync(string credentialsJsonPath, string tokenStorePath = "TokenStore", CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(credentialsJsonPath))
            throw new ArgumentException("Credentials JSON path is required", nameof(credentialsJsonPath));
        if (!File.Exists(credentialsJsonPath))
            throw new FileNotFoundException("Credentials JSON not found", credentialsJsonPath);

        await using var stream = new FileStream(credentialsJsonPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var doc = await System.Text.Json.JsonDocument.ParseAsync(stream, cancellationToken: ct);
        stream.Position = 0;
        var secrets = await GoogleClientSecrets.FromStreamAsync(stream, ct).ConfigureAwait(false);

        Directory.CreateDirectory(tokenStorePath);

        var cred = await GoogleWebAuthorizationBroker.AuthorizeAsync(
            secrets.Secrets,
            Scopes,
            user: "default",
            taskCancellationToken: ct,
            dataStore: new FileDataStore(tokenStorePath, fullPath: true)
        ).ConfigureAwait(false);

        var apiService = new Google.Apis.Gmail.v1.GmailService(new BaseClientService.Initializer
        {
            HttpClientInitializer = cred,
            ApplicationName = "PrivateAgent Gmail Integration"
        });

        return new GmailService(apiService);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(GmailService));
    }

    /// <inheritdoc />
    public async Task<IList<Message>> SearchMessagesAsync(string query, int? maxResults = null, CancellationToken ct = default)
    {
        ThrowIfDisposed();

        var results = new List<Message>();
        var request = _service.Users.Messages.List("me");
        if (!string.IsNullOrWhiteSpace(query))
            request.Q = query;
        request.IncludeSpamTrash = false;
        request.MaxResults = maxResults.HasValue ? Math.Min(maxResults.Value, 500) : null;

        string? pageToken = null;
        int collected = 0;
        do
        {
            ct.ThrowIfCancellationRequested();
            request.PageToken = pageToken;
            var response = await request.ExecuteAsync(ct).ConfigureAwait(false);
            if (response?.Messages != null)
            {
                foreach (var m in response.Messages)
                {
                    results.Add(m);
                    collected++;
                    if (maxResults.HasValue && collected >= maxResults.Value) break;
                }
            }
            pageToken = response?.NextPageToken;
            if (maxResults.HasValue && collected >= maxResults.Value) break;
        } while (!string.IsNullOrEmpty(pageToken));

        return results;
    }

    /// <inheritdoc />
    public async Task<Message> GetMessageAsync(string messageId, string format = "full", CancellationToken ct = default)
    {
        ThrowIfDisposed();
        if (string.IsNullOrWhiteSpace(messageId)) throw new ArgumentException("Message id required", nameof(messageId));
        var req = _service.Users.Messages.Get("me", messageId);
        req.Format = format?.ToLowerInvariant() switch
        {
            "minimal" => UsersResource.MessagesResource.GetRequest.FormatEnum.Minimal,
            "raw" => UsersResource.MessagesResource.GetRequest.FormatEnum.Raw,
            "metadata" => UsersResource.MessagesResource.GetRequest.FormatEnum.Metadata,
            _ => UsersResource.MessagesResource.GetRequest.FormatEnum.Full
        };
        return await req.ExecuteAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Returns decoded plain text body (best-effort) plus Date header (sender supplied) if present using raw MIME + MimeKit.
    /// </summary>
    public async Task<MessageTextAndDetails> GetMessageTextAsync(string messageId, CancellationToken ct = default)
    {
        // Ask Gmail for the full raw MIME message
        var request = _service.Users.Messages.Get("me", messageId);
        request.Format = UsersResource.MessagesResource.GetRequest.FormatEnum.Raw;
        var message = await request.ExecuteAsync(ct).ConfigureAwait(false);

        if (string.IsNullOrEmpty(message.Raw))
            return new MessageTextAndDetails(null, null);

        // Gmail returns raw MIME in base64url encoding, fix + decode
        string raw = message.Raw.Replace('-', '+').Replace('_', '/');
        byte[] rawBytes = Convert.FromBase64String(PadBase64(raw));

        // Parse MIME using MimeKit
        var mime = MimeMessage.Load(new MemoryStream(rawBytes));

        // Extract plain text, falling back to HTML if needed
        string? plain = mime.TextBody ?? (!string.IsNullOrWhiteSpace(mime.HtmlBody) ? HtmlToPlain(mime.HtmlBody) : null);

        // Date header (sender-supplied)
        DateTimeOffset? headerDate = mime.Date != DateTimeOffset.MinValue ? mime.Date : (DateTimeOffset?)null;

        return new MessageTextAndDetails(plain, headerDate);
    }

    /// <summary>
    /// Returns message text plus attachments (if any). Attachments include Base64 data.
    /// </summary>
    public async Task<MessageTextAndDetailsWithAttachments> GetMessageTextAndAttachmentsAsync(string messageId, CancellationToken ct = default)
    {
        // Use full to traverse parts for attachments & text
        var msg = await GetMessageAsync(messageId, "full", ct).ConfigureAwait(false);

        string? plain = null;
        // Locate text/plain part first; fallback to HTML->text
        if (msg.Payload != null)
        {
            plain = FindFirstPartData(msg.Payload, "text/plain") is string plainData
                ? DecodeBase64UrlToString(plainData)
                : null;

            if (plain == null)
            {
                var htmlData = FindFirstPartData(msg.Payload, "text/html");
                if (htmlData != null)
                {
                    var html = DecodeBase64UrlToString(htmlData);
                    plain = HtmlToPlain(html ?? string.Empty);
                }
            }
        }

        // Date header
        DateTimeOffset? headerDate = null;
        var dateHeader = msg.Payload?.Headers?.FirstOrDefault(h => h.Name.Equals("Date", StringComparison.OrdinalIgnoreCase))?.Value;
        if (!string.IsNullOrWhiteSpace(dateHeader) && DateTimeOffset.TryParse(dateHeader, out var parsed))
            headerDate = parsed;

        // Collect attachment metadata parts
        var attachmentParts = EnumerateParts(msg.Payload)
            .Where(p => !string.IsNullOrEmpty(p.Filename) && p.Body?.AttachmentId != null)
            .ToList();

        var attachments = new List<AttachmentInfo>(attachmentParts.Count);
        foreach (var part in attachmentParts)
        {
            ct.ThrowIfCancellationRequested();
            var body = await _service.Users.Messages.Attachments.Get("me", messageId, part.Body.AttachmentId).ExecuteAsync(ct).ConfigureAwait(false);
            if (body?.Data == null) continue;
            var bytes = DecodeBase64UrlToBytes(body.Data);
            attachments.Add(new AttachmentInfo(part.Filename, part.MimeType, Convert.ToBase64String(bytes), bytes.Length));
        }

        return new MessageTextAndDetailsWithAttachments(plain, headerDate, attachments);
    }

    private static IEnumerable<Google.Apis.Gmail.v1.Data.MessagePart> EnumerateParts(Google.Apis.Gmail.v1.Data.MessagePart? root)
    {
        if (root == null) yield break;
        yield return root;
        if (root.Parts != null)
        {
            foreach (var p in root.Parts)
                foreach (var c in EnumerateParts(p))
                    yield return c;
        }
    }

    private static string PadBase64(string s)
    {
        int padding = 4 - (s.Length % 4);
        return (padding is > 0 and < 4) ? s.PadRight(s.Length + padding, '=') : s;
    }

    private static byte[] DecodeBase64UrlToBytes(string input)
    {
        var s = input.Replace('-', '+').Replace('_', '/');
        s = PadBase64(s);
        return Convert.FromBase64String(s);
    }

    private static string? DecodeBase64UrlToString(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        try { return Encoding.UTF8.GetString(DecodeBase64UrlToBytes(input)); }
        catch { return null; }
    }

    private static string HtmlToPlain(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return string.Empty;
        var text = System.Text.RegularExpressions.Regex.Replace(html, "<.*?>", string.Empty);
        text = System.Net.WebUtility.HtmlDecode(text);
        return string.Join(
            Environment.NewLine,
            text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim()))
            .Trim();
    }

    private static string? FindFirstPartData(Google.Apis.Gmail.v1.Data.MessagePart part, string targetMime)
    {
        if (part == null) return null;
        if (!string.IsNullOrEmpty(part.Filename) && part.Body?.AttachmentId != null) return null; // skip attachments
        if (part.MimeType != null && part.MimeType.Equals(targetMime, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(part.Body?.Data))
            return part.Body.Data;
        if (part.Parts != null)
        {
            foreach (var p in part.Parts)
            {
                var found = FindFirstPartData(p, targetMime);
                if (found != null) return found;
            }
        }
        return null;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _service.Dispose();
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }
}

public interface IGmailService
{
    Task<IList<Message>> SearchMessagesAsync(string query, int? maxResults = null, CancellationToken ct = default);
    Task<Message> GetMessageAsync(string messageId, string format = "full", CancellationToken ct = default);
    Task<MessageTextAndDetails> GetMessageTextAsync(string messageId, CancellationToken ct = default);
    Task<MessageTextAndDetailsWithAttachments> GetMessageTextAndAttachmentsAsync(string messageId, CancellationToken ct = default);
}

public sealed record MessageTextAndDetails(string? PlainText, DateTimeOffset? HeaderDate);
public sealed record AttachmentInfo(string FileName, string? MimeType, string Base64Data, int SizeBytes);
public sealed record MessageTextAndDetailsWithAttachments(string? PlainText, DateTimeOffset? HeaderDate, IReadOnlyList<AttachmentInfo> Attachments);