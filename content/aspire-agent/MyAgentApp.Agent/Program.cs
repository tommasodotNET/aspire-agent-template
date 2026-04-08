using Microsoft.Agents.AI;
using Microsoft.Agents.AI.DevUI;
using Microsoft.Agents.AI.Hosting;
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
// Azure OpenAI connection string. Set in AppHost user-secrets:
//   cd MyAgentApp.AppHost
//   dotnet user-secrets set "ConnectionStrings:openai" "Endpoint=https://your-resource.openai.azure.com/"
#else
// OpenAI API connection string. Set in AppHost user-secrets:
//   cd MyAgentApp.AppHost
//   dotnet user-secrets set "ConnectionStrings:openai" "Endpoint=https://api.openai.com/v1;Key=sk-your-key"
//   For GitHub Models: "Endpoint=https://models.inference.ai.azure.com;Key=ghp_your-token"
#endif

#if (UseAnyFoundry)
{
    builder.AddAIAgent("MyAgent", (sp, name) =>
    {
        var chatClient = sp.GetRequiredService<IChatClient>();
        return chatClient.AsAIAgent(
            name: name,
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
    ? "⚠️ Agent Service is running but AI is not configured. Set ConnectionStrings:openai to your Azure OpenAI endpoint in AppHost user-secrets."
#else
    ? "⚠️ Agent Service is running but AI is not configured. Set ConnectionStrings:openai with your OpenAI API key in AppHost user-secrets."
#endif
    : "Agent Service is running. DevUI at /devui.");

app.Run();
