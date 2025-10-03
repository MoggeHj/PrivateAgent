using Agent;
using Microsoft.Extensions.AI;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddControllers();
builder.Services.AddOpenApi();

await Startup.ConfigureServices(builder.Services);

var systemPrompt = "You are a helpful agent that can look up things in my email account and keep a good polite conversation";

var app = builder.Build();

app.MapPost("/chat", async (
    List<ChatMessage> messages,
    IChatClient client,
    ChatOptions? chatOptions) =>
{
    if (messages is null || messages.Count == 0)
    {
        return Results.BadRequest("Messages collection cannot be empty.");
    }

    //Add date and incoming message to system prompt
    var systemPromptWithDate = systemPrompt + "\nBy the way today's date is " + DateTime.Now.ToLongDateString();

    var withSystemPrompt = new List<ChatMessage>(messages.Count + 1)
    {
        new(ChatRole.System, systemPromptWithDate)
    };
    withSystemPrompt.AddRange(messages);

    // Call the chat model
    var response = await client.GetResponseAsync(withSystemPrompt, chatOptions);

    
    //Create 
    var chatResponse = new List<SimpleMessage>(response.Messages.Count);

    foreach (var responseMessage in response.Messages)
    {
        chatResponse.Add(new SimpleMessage(
            responseMessage.Role.ToString().ToLowerInvariant(),
            responseMessage.Text));
    }

    return Results.Ok(chatResponse);
});

app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();
app.Run();

public record SimpleMessage(string Role, string Text);
