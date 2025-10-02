using System.ComponentModel;
using McpServer;
using McpServer.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Server;

var builder = WebApplication.CreateBuilder(args);

// Register your MCP server + discover tools in the assembly
builder.Services
    .AddMcpServer()
    .WithHttpTransport()
    .WithPromptsFromAssembly()
    .WithResourcesFromAssembly()
    .WithToolsFromAssembly(); // scans for [McpServerTool] methods

builder.WebHost.UseUrls("http://localhost:5050");

// Gmail service (async factory -> sync wait). Configure paths via config/env.
builder.Services.AddSingleton<IGmailService>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var credentialsPath = config["GMAIL_CREDENTIALS_PATH"] ?? "credentials.json"; // place your client_secret.json
    var tokenStore = config["GMAIL_TOKEN_STORE"] ?? "TokenStore"; // directory for stored tokens
    return GmailService.CreateAsync(credentialsPath, tokenStore).GetAwaiter().GetResult();
});

builder.Services.AddSingleton<IMcpTools, McpTools>();

var app = builder.Build();

// Map the MCP endpoints for Streamable HTTP
// This adds the required /sse (events) and /messages endpoints.
app.MapMcp();

// (Optional) protect your MCP endpoints
// app.MapMcp().RequireAuthorization();

app.Run();
