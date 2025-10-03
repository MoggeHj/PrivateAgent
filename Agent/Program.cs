using Agent;
using Microsoft.Extensions.AI;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddControllers();
builder.Services.AddOpenApi();

await Startup.ConfigureServices(builder.Services);

var systemPrompt = "You are an helpful agent that can look up things in my email account and keep a good polite conversation";

var app = builder.Build();

app.MapPost("/chat", async (
    List<ChatMessage> messages,
    IChatClient client,
    ChatOptions chatOptions) =>
{
    var systemPromptWithDate = systemPrompt + "\nBy the way today's date is " + DateTime.Now.ToLongDateString();

    var withSystemPrompt = new List<ChatMessage>(messages.Count + 1)
    {
        new(ChatRole.System, systemPromptWithDate)
    };
    withSystemPrompt.AddRange(messages);

    var response = await client.GetResponseAsync(withSystemPrompt, chatOptions);

    

    var messagesResult = new List<SimpleMessage>();

    foreach (var responseMessage in response.Messages)
    {
        messagesResult.Add(new SimpleMessage(
        
            Role = responseMessage.Role
                .ToString()
                .ToLowerInvariant(),
            Text = responseMessage.Text
        ));
    }

    return Results.Ok(messagesResult);
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

public record Messages (List<SimpleMessage> Items);
