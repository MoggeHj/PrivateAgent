using Anthropic.SDK;
using GeminiDotnet;
using GeminiDotnet.Extensions.AI;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using ModelContextProtocol.Server;

namespace Agent;

public static class Startup
{
    public static async Task ConfigureServices(IServiceCollection services)
    {
        var openAiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")!;
        var geminiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY")!;
        var claudeKey = Environment.GetEnvironmentVariable("CLAUDE_API_KEY")!;
        var provider = Environment.GetEnvironmentVariable("PROVIDER");
        var model = Environment.GetEnvironmentVariable("MODEL");
        var mcpServerUrl = Environment.GetEnvironmentVariable("MCP_SERVER_URL") ?? "http://localhost:5050"; // default to local MCP server

        services.AddLogging(logging => logging.AddConsole().SetMinimumLevel(LogLevel.Information));
        services.AddSingleton<ILoggerFactory>(sp =>
            LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information)));

        services.AddSingleton<IChatClient>(sp =>
        {
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
            var client = provider switch
            {
                "openai" => new OpenAI.Chat.ChatClient(
                    string.IsNullOrWhiteSpace(model) ? "gpt-4.1-mini" : model,
                    openAiKey).AsIChatClient(),

                "gemini" => new GeminiChatClient(new GeminiDotnet.GeminiClientOptions { ApiKey = geminiKey, ModelId = model, ApiVersion = GeminiApiVersions.V1Beta }),

                "claude" => new AnthropicClient(new APIAuthentication(claudeKey)).Messages,

                _ => throw new ArgumentException($"Unknown provider: {provider}")
            };

            return new ChatClientBuilder(client)
                .UseLogging(loggerFactory)
                .UseFunctionInvocation(loggerFactory, c =>
                {
                    c.IncludeDetailedErrors = true;
                })
                .Build(sp);
        });

        // Connect to existing MCP server over HTTP instead of spawning a stdio process
        var mcpClient = await McpClientFactory.CreateAsync(
            new HttpClientTransport(new HttpClientTransportOptions
            {
                Endpoint = new Uri(mcpServerUrl)
            })
        );

        // List available tools from remote MCP server
        var tools = await mcpClient.ListToolsAsync();
        foreach (var tool in tools)
        {
            Console.WriteLine($"{tool.Name}: {tool.Description}");
        }

        services.AddTransient<ChatOptions>(sp => new ChatOptions
        {
            Tools = [..tools],
            ModelId = model,
            Temperature = 1,
            MaxOutputTokens = 5000
        });
    }
}

public static class FunctionRegistry
{
    public static IEnumerable<AITool> GetTools(IServiceProvider sp)
    {
        throw new NotImplementedException();
    }
}