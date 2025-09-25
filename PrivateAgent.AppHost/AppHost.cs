var builder = DistributedApplication.CreateBuilder(args);

var provider = builder.AddParameter("Provider", secret: false);
var model = builder.AddParameter("Model", secret: false);

builder.AddProject<Projects.Agent>("agent")
    .WithEnvironment("PROVIDER", provider)
    .WithEnvironment("MODEL", model);

builder.Build().Run();
