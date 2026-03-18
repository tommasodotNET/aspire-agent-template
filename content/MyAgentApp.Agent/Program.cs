using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.DevUI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Extensions.AI;
using MyAgentApp.Agent;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// ── Domain Services ─────────────────────────────────────────────────────────
// Register your domain services here. The AI agent calls these through tools.
// Architecture: API request → Agent → Tools → Domain Service

builder.Services.AddSingleton<TodoService>();

// ── AI Agent Configuration ──────────────────────────────────────────────────
// Configure the AI agent with Azure OpenAI (or swap to OpenAI / Ollama).
// Set these in user-secrets or environment variables:
//   dotnet user-secrets set "AzureOpenAI:Endpoint" "https://your-resource.openai.azure.com"
//   dotnet user-secrets set "AzureOpenAI:Deployment" "gpt-4o-mini"

var endpoint = builder.Configuration["AzureOpenAI:Endpoint"];
var deployment = builder.Configuration["AzureOpenAI:Deployment"] ?? "gpt-4o-mini";

if (!string.IsNullOrEmpty(endpoint))
{
    builder.Services.AddSingleton<IChatClient>(sp =>
    {
        return new AzureOpenAIClient(new Uri(endpoint), new AzureCliCredential())
            .GetChatClient(deployment)
            .AsIChatClient();
    });

    // Register the agent using the Hosting pattern so DevUI can discover it.
    // builder.AddAIAgent registers a keyed AIAgent service.
    builder.AddAIAgent("MyAgent", (sp, name) =>
    {
        var chatClient = sp.GetRequiredService<IChatClient>();
        var todoTools = new TodoTools(sp.GetRequiredService<TodoService>());
        return chatClient.AsAIAgent(
            name: name,
            instructions: """
                You are a helpful AI assistant that manages a todo list.
                Use the available tools to add, list, complete, and delete todo items.
                Be friendly, concise, and helpful. When listing todos, format them clearly.
                """,
            tools: todoTools.AsAIFunctions());
    });
}

// ── OpenAI-compatible API (required by DevUI) ───────────────────────────────
// These services expose the agent via OpenAI Responses/Conversations protocol,
// which DevUI uses to communicate with the agent.
builder.Services.AddOpenAIResponses();
builder.Services.AddOpenAIConversations();

var app = builder.Build();

app.MapDefaultEndpoints();

// Map OpenAI-compatible endpoints (required by DevUI)
app.MapOpenAIResponses();
app.MapOpenAIConversations();

// ── DevUI (Development only) ────────────────────────────────────────────────
// DevUI provides a web interface for testing and debugging the agent —
// inspect tools, trace calls, and chat without the Blazor UI.
// Access it at: {agent-service-url}/devui
if (app.Environment.IsDevelopment())
{
    app.MapDevUI();
}

// ── Chat Endpoint ───────────────────────────────────────────────────────────
// POST /api/chat  { "messages": [{ "role": "user", "content": "Hello!" }] }
// Returns { "response": "Hi there! How can I help?" }
//
// The messages array supports multi-turn conversations — send the full
// conversation history so the agent can reference prior context.

app.MapPost("/api/chat", async (ChatRequest request, IServiceProvider sp) =>
{
    var agent = sp.GetKeyedService<AIAgent>("MyAgent");
    if (agent is null)
    {
        return Results.Json(new { response = "⚠️ Agent not configured. Set AzureOpenAI:Endpoint via user-secrets. See README for details." },
            statusCode: 503);
    }

    // Build conversation history for multi-turn context
    var chatMessages = request.Messages
        .Select(m => new ChatMessage(
            m.Role == "user" ? ChatRole.User : ChatRole.Assistant,
            m.Content))
        .ToList();

    var response = await agent.RunAsync(chatMessages);
    return Results.Ok(new { response = response.Text });
});

app.MapGet("/", (IServiceProvider sp) => sp.GetKeyedService<AIAgent>("MyAgent") is null
    ? "⚠️ Agent Service is running but AI is not configured. Set AzureOpenAI:Endpoint via user-secrets."
    : "Agent Service is running. POST to /api/chat to interact.");

app.Run();

public record ChatMessageDto(string Role, string Content);
public record ChatRequest(List<ChatMessageDto> Messages);
