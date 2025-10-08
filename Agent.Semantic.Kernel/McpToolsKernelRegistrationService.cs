using Microsoft.SemanticKernel;
using ModelContextProtocol.Client;
using System.Text.Json;

namespace Agent.Semantic.Kernel;

// Registers MCP server tools as strongly-typed Semantic Kernel functions so the model sees real parameters.
public class McpToolsKernelRegistrationService : IHostedService
{
    private readonly IMcpClient _mcpClient;
    private readonly KernelPluginCollection _pluginCollection;
    private readonly ILogger<McpToolsKernelRegistrationService> _logger;

    public McpToolsKernelRegistrationService(
        IMcpClient mcpClient,
        KernelPluginCollection pluginCollection,
        ILogger<McpToolsKernelRegistrationService> logger)
    {
        _mcpClient = mcpClient;
        _pluginCollection = pluginCollection;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Discovering MCP tools to register as Semantic Kernel plugin...");

        var tools = await _mcpClient.ListToolsAsync(cancellationToken: cancellationToken);
        if (tools.Count == 0)
        {
            _logger.LogWarning("No MCP tools discovered.");
            return;
        }

        var functions = new List<KernelFunction>();

        foreach (var tool in tools)
        {
            var name = tool.Name;
            var description = tool.Description ?? name;

            KernelFunction fn;

            // Hand-map known tools to strongly typed signatures so the LLM knows the individual parameters.
            // This fixes the issue where only maxResults was being passed because the schema only exposed a single 'payload' parameter.
            switch (name)
            {
                case "search_messages":
                    fn = KernelFunctionFactory.CreateFromMethod(
                        (Func<Microsoft.SemanticKernel.Kernel, string, int?, Task<object?>>)((kernel, query, maxResults) =>
                            InvokeToolAsync(name, new Dictionary<string, object?>
                            {
                                ["query"] = query,
                                ["maxResults"] = maxResults
                            })),
                        functionName: name,
                        description: description,
                        parameters: new[]
                        {
                            new KernelParameterMetadata("query")
                            {
                                Description = "Search query string.",
                                ParameterType = typeof(string),
                                IsRequired = true
                            },
                            new KernelParameterMetadata("maxResults")
                            {
                                Description = "Optional maximum number of results.",
                                ParameterType = typeof(int),
                                IsRequired = false
                            }
                        }
                    );
                    break;

                case "get_message_content":
                    fn = KernelFunctionFactory.CreateFromMethod(
                        (Func<Microsoft.SemanticKernel.Kernel, string, Task<object?>>)((kernel, messageId) =>
                            InvokeToolAsync(name, new Dictionary<string, object?>
                            {
                                ["messageId"] = messageId
                            })),
                        functionName: name,
                        description: description,
                        parameters: new[]
                        {
                            new KernelParameterMetadata("messageId")
                            {
                                Description = "Gmail message ID.",
                                ParameterType = typeof(string),
                                IsRequired = true
                            }
                        }
                    );
                    break;

                case "get_message_content_with_attachments":
                    fn = KernelFunctionFactory.CreateFromMethod(
                        (Func<Microsoft.SemanticKernel.Kernel, string, Task<object?>>)((kernel, messageId) =>
                            InvokeToolAsync(name, new Dictionary<string, object?>
                            {
                                ["messageId"] = messageId
                            })),
                        functionName: name,
                        description: description,
                        parameters: new[]
                        {
                            new KernelParameterMetadata("messageId")
                            {
                                Description = "Gmail message ID.",
                                ParameterType = typeof(string),
                                IsRequired = true
                            }
                        }
                    );
                    break;

                default:
                    // Fallback: keep generic JSON payload for any other tools.
                    fn = KernelFunctionFactory.CreateFromMethod(
                        (Func<Microsoft.SemanticKernel.Kernel, string?, Task<object?>>)((kernel, payload) =>
                            GenericJsonInvoker(name, payload)),
                        functionName: name,
                        description: description + " (generic JSON wrapper)",
                        parameters: new[]
                        {
                            new KernelParameterMetadata("payload")
                            {
                                Description = "Raw JSON object of arguments for this MCP tool.",
                                ParameterType = typeof(string),
                                IsRequired = false
                            }
                        }
                    );
                    break;
            }

            functions.Add(fn);
        }

        var plugin = KernelPluginFactory.CreateFromFunctions(
            pluginName: "mcp",
            description: "Remote MCP server tools",
            functions: functions);

        _pluginCollection.Add(plugin);

        foreach (var f in plugin)
        {
            _logger.LogInformation("Registered SK function: {Name}", f.Name);
            foreach (var p in f.Metadata.Parameters)
            {
                _logger.LogInformation("  Param: {Name} ({Type}) Required={Required}",
                    p.Name, p.ParameterType, p.IsRequired);
            }
        }

        _logger.LogInformation("Registered {Count} MCP tools into Semantic Kernel plugin 'mcp'.", functions.Count);
    }

    // Retained for generic fallback tools.
    private async Task<object?> GenericJsonInvoker(string toolName, string? payload)
    {
        Dictionary<string, object?> args = new();
        if (!string.IsNullOrWhiteSpace(payload))
        {
            try
            {
                args = JsonSerializer.Deserialize<Dictionary<string, object?>>(payload!) ?? new();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse payload for {Tool}", toolName);
            }
        }
        return await InvokeToolAsync(toolName, args);
    }

    private async Task<object?> InvokeToolAsync(string toolName, Dictionary<string, object?> rawArgs)
    {
        var filtered = rawArgs
            .Where(kv => kv.Value is not null)
            .ToDictionary(
                kv => kv.Key.Length > 0
                    ? char.ToLowerInvariant(kv.Key[0]) + kv.Key.Substring(1)
                    : kv.Key,
                kv => kv.Value);

        _logger.LogDebug("Invoking MCP tool {Tool} with args: {Args}", toolName, Safe(filtered));

        try
        {
            var result = await _mcpClient.CallToolAsync(toolName, filtered, cancellationToken: CancellationToken.None);
            if (result is null) return "[MCP] (no result)";
            return JsonSerializer.Serialize(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MCP tool {Tool} invocation failed", toolName);
            return $"[MCP ERROR] {ex.Message}";
        }
    }

    private static string Safe(object obj)
    {
        try { return JsonSerializer.Serialize(obj); } catch { return obj.ToString() ?? "<obj>"; }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}