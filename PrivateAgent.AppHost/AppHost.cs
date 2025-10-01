using Aspire.Hosting;
using AzureKeyVaultEmulator.Aspire.Hosting;
using dotenv.net;

var builder = DistributedApplication.CreateBuilder(args);

DotEnv.Load();

var provider = builder.AddParameter("Provider", secret: false);
var model = builder.AddParameter("Model", secret: false);
var openAiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");

builder.AddProject<Projects.Agent>("agent")
    .WithEnvironment("PROVIDER", provider)
    .WithEnvironment("MODEL", model)
    .WithEnvironment("OPENAI_API_KEY", openAiKey);

builder.Build().Run();
