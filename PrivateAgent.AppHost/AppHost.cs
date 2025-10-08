using Aspire.Hosting;
using AzureKeyVaultEmulator.Aspire.Hosting;
using dotenv.net;

var builder = DistributedApplication.CreateBuilder(args);

DotEnv.Load();

var provider = builder.AddParameter("Provider", secret: false);
var model = builder.AddParameter("Model", secret: false);
var openAiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
var mcpServerUrl = builder.AddParameter("McpServerUrl", secret: false);

var mcpServer = builder.AddProject<Projects.McpServer>("mcpserver");

var agent = builder.AddProject<Projects.Agent>("agent")
    .WithEnvironment("PROVIDER", provider)
    .WithEnvironment("MODEL", model)
    .WithEnvironment("OPENAI_API_KEY", openAiKey)
    .WithEnvironment("MCP_SERVER_URL", mcpServerUrl)
    .WaitFor(mcpServer);



builder.AddProject<Projects.ChatClient>("chatclient")
    .WaitFor(agent);



builder.AddProject<Projects.Agent_Semantic_Kernel>("agent-semantic-kernel")
    .WithEnvironment("OPENAI_API_KEY", openAiKey)
    .WithEnvironment("MODEL", model)
    .WaitFor(mcpServer);



builder.Build().Run();
