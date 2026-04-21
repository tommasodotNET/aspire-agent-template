using A2A;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.DevUI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Agents.AI.Hosting.A2A;
using Microsoft.Agents.AI.Hosting.AGUI.AspNetCore;
using Microsoft.Extensions.AI;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// ── LLM Client (Aspire-native) ──────────────────────────────────────────────
#if (UseAnyFoundry)
// Foundry: IChatClient is registered automatically via Aspire service discovery.
// The deployment is declared in the AppHost — no manual config needed here.
builder.AddAzureChatCompletionsClient("chat")
    .AddChatClient("chat");
#elif (UseAzureOpenAI)
// Azure OpenAI — connection string configured in the AppHost.
// Set ConnectionStrings:openai in user-secrets or enter in the Aspire dashboard.
#else
// OpenAI API — connection string configured in the AppHost.
// Set ConnectionStrings:openai in user-secrets or enter in the Aspire dashboard.
#endif

#if (UseAnyFoundry)
{
    builder.AddAIAgent("MyAgent", (sp, name) =>
    {
        var chatClient = sp.GetRequiredService<IChatClient>();
        return chatClient.AsAIAgent(
            name: name,
            description: "A helpful AI assistant that answers questions clearly and concisely.",
            instructions: """
                You are a helpful AI assistant. Answer questions clearly and concisely.
                """);
    });
}
#else
var connectionString = builder.Configuration.GetConnectionString("openai");
if (!string.IsNullOrEmpty(connectionString))
{
#if (UseOpenAI)
    builder.AddOpenAIClientFromConfiguration("openai");
#else
    builder.AddAzureOpenAIClient("openai");
#endif

    var deployment = builder.Configuration["OpenAI:Deployment"] ?? "gpt-4o-mini";
    builder.AddAIAgent("MyAgent", (sp, name) =>
    {
        var openaiClient = sp.GetRequiredService<OpenAI.OpenAIClient>();
        var chatClient = openaiClient.GetChatClient(deployment).AsIChatClient();
        return chatClient.AsAIAgent(
            name: name,
            description: "A helpful AI assistant that answers questions clearly and concisely.",
            instructions: """
                You are a helpful AI assistant. Answer questions clearly and concisely.
                """);
    });
}
#endif

// ── AG-UI Protocol ───────────────────────────────────────────────────────────
builder.Services.AddAGUI();

// ── OpenAI-compatible API (required by DevUI) ───────────────────────────────
builder.Services.AddOpenAIResponses();
builder.Services.AddOpenAIConversations();

var app = builder.Build();

app.MapDefaultEndpoints();

var agent = app.Services.GetKeyedService<AIAgent>("MyAgent");
if (agent is not null)
{
    app.MapAGUI("/api/agui", agent);

    // ── A2A Protocol ─────────────────────────────────────────────────────────
    app.MapA2A(agent, "/api/a2a", new AgentCard
    {
        Name = agent.Name,
        Description = agent.Description,
        Version = "1.0",
        DefaultInputModes = ["text"],
        DefaultOutputModes = ["text"],
        Capabilities = new AgentCapabilities
        {
            Streaming = true,
            PushNotifications = false
        }
    });
}

app.MapOpenAIResponses();
app.MapOpenAIConversations();

if (app.Environment.IsDevelopment())
{
    app.MapDevUI();
}

app.MapGet("/", (IServiceProvider sp) => sp.GetKeyedService<AIAgent>("MyAgent") is null
#if (UseAnyFoundry)
    ? "⚠️ Agent Service is running but AI is not configured. Check Foundry resource in AppHost."
#elif (UseAzureOpenAI)
    ? "⚠️ Agent Service is running but AI is not configured. Set ConnectionStrings:openai in AppHost user-secrets or enter in the Aspire dashboard."
#else
    ? "⚠️ Agent Service is running but AI is not configured. Set ConnectionStrings:openai in AppHost user-secrets or enter in the Aspire dashboard."
#endif
    : "Agent Service is running. DevUI at /devui.");

app.Run();
