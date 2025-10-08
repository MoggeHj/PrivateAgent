using Agent.Semantic.Kernel;
using Microsoft.Extensions.AI;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddControllers();
builder.Services.AddOpenApi();

Startup.ConfigureServices(builder.Services);

// System prompt for the assistant
const string systemPrompt = "You are a helpful agent that can use available tools to answer user questions politely.";

var app = builder.Build();

app.MapPost("/chat", async (
    List<ChatMessage> messages,
    Kernel kernel) =>
{
    if (messages is null || messages.Count == 0)
    {
        return Results.BadRequest("Messages collection cannot be empty.");
    }

    var history = new ChatHistory();
    history.AddSystemMessage(systemPrompt + "\nToday's date is " + DateTime.Now.ToLongDateString());

    foreach (var m in messages)
    {
        var role = m.Role.Value.ToLower();
        var text = m.Text ?? string.Empty;
        switch (role)
        {
            case "system":
                history.AddSystemMessage(text);
                break;
            case "assistant":
                history.AddAssistantMessage(text);
                break;
            case "tool":
                // No dedicated tool role in ChatHistory; append as system note
                history.AddSystemMessage("[Tool Result] " + text);
                break;
            default:
                history.AddUserMessage(text);
                break;
        }
    }

    // Enable automatic invocation of registered kernel functions (MCP tools wrapped as functions)
    var settings = new OpenAIPromptExecutionSettings
    {
        ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions,
        Temperature = 0.8,
    };
   
    var chatService = kernel.GetRequiredService<IChatCompletionService>();
    var result = await chatService.GetChatMessageContentsAsync(history, executionSettings: settings, kernel: kernel);

    // Append assistant messages to history is optional here (client maintains state externally)
    var responseMessages = new List<SimpleMessage>(result.Count);
    foreach (var msg in result)
    {
        // Only return assistant output (ignore tool messages etc.)
        if (msg.Role == AuthorRole.Assistant)
        {
            responseMessages.Add(new SimpleMessage("assistant", msg.Content ?? string.Empty));
        }
    }

    if (responseMessages.Count == 0)
    {
        // Fallback: return concatenated content if roles filtered everything
        var combined = string.Join('\n', result.Select(r => r.Content));
        responseMessages.Add(new SimpleMessage("assistant", combined));
    }

    return Results.Ok(responseMessages);
});

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

public record ClientMessage(string Role, string Text);
public record SimpleMessage(string Role, string Text);
