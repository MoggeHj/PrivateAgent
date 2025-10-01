using Agent;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using AzureKeyVaultEmulator.Aspire.Client;
using Grpc.Core;
using Microsoft.AspNetCore.Connections.Features;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

//Configure agent
await Startup.ConfigureServices(builder.Services);

var app = builder.Build();


app.MapDefaultEndpoints();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
