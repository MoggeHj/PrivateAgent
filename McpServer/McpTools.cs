using System.ComponentModel;
using Google.Apis.Gmail.v1.Data;
using McpServer.Services;
using ModelContextProtocol.Server;

namespace McpServer
{
    [McpServerToolType]
    public class McpTools : IMcpTools
    {
        private readonly IGmailService _gmailService;

        public McpTools(IGmailService gmailService)
        {
            _gmailService = gmailService;
        }

        [McpServerTool, Description("Search for mails that matches the query parameter.")]
        public async Task<List<Message>> SearchMessagesAsync([Description("searches the mail inbox after this query paramter")]string query)
        {
            var maxResults = 2;
            var result = await _gmailService.SearchMessagesAsync(query, maxResults);

            return result.ToList();
        }

        [McpServerTool, Description("Get the mail plain text and date the mail was sent for a mail which is queried by messageId property")]
        public async Task<MessageTextAndDetails> GetMessageContentAsync(string messageId)
        {
            var details = await _gmailService.GetMessageTextAsync(messageId);
            return details;
        }

        [McpServerTool, Description("Get a mail by messageId. Returns the mails decoded plain text, header date and any attachments (base64) for a message")]
        public async Task<MessageTextAndDetailsWithAttachments> GetMessageContentWithAttachmentsAsync(string messageId)
        {
            var details = await _gmailService.GetMessageTextAndAttachmentsAsync(messageId);
            return details;
        }
    }

    public interface IMcpTools
    {
        Task<List<Message>> SearchMessagesAsync(string query);

        Task<MessageTextAndDetails> GetMessageContentAsync(string messageId);

        Task<MessageTextAndDetailsWithAttachments> GetMessageContentWithAttachmentsAsync(string messageId);
        //Task<Message> GetMessageAsync(string messageId);
    }
}