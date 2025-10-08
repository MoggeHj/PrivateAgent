using System.Runtime.CompilerServices;
using Microsoft.SemanticKernel;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using HttpClientTransport = Azure.Core.Pipeline.HttpClientTransport;

namespace Agent.Semantic.Kernel;

public class Startup
{
    public static void ConfigureServices(IServiceCollection services)
    {
        var openAiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (string.IsNullOrWhiteSpace(openAiKey))
        {
            throw new InvalidOperationException("Environment variable OPENAI_API_KEY is not set.");
        }
            
        var model = Environment.GetEnvironmentVariable("MODEL") ?? "gpt-4o-mini";
        var mcpServerUrl = Environment.GetEnvironmentVariable("MCP_SERVER_URL") ?? "http://localhost:5050"; // default to local MCP server

        services.AddLogging(logging =>
        {
            logging.ClearProviders();
            logging.AddConsole();
            logging.SetMinimumLevel(LogLevel.Information);
        });

        // Ensure plugin collection exists (Semantic Kernel registers it in some helpers;
        // include explicitly for clarity / safety).
        services.AddSingleton<KernelPluginCollection>();

        // Register OpenAI chat completion
        services.AddOpenAIChatCompletion(modelId: model, apiKey: openAiKey);

        //Register MCP Client
        services.AddSingleton<IMcpClient>(sp =>
        {
            var httpClientTransportOptions = new HttpClientTransportOptions
            {
                Endpoint = new Uri(mcpServerUrl)
            };
            var clientTransport = new ModelContextProtocol.Client.HttpClientTransport(httpClientTransportOptions);
            var client = McpClient.CreateAsync(clientTransport);
            
            return client.GetAwaiter().GetResult();
        });

        services.AddTransient((serviceProvider) => {
            KernelPluginCollection pluginCollection = serviceProvider.GetRequiredService<KernelPluginCollection>();

            return new Microsoft.SemanticKernel.Kernel(serviceProvider, pluginCollection);
        });

        // Register hosted service to handle MCP tool registration
        services.AddHostedService<McpToolsKernelRegistrationService>();
    }
}