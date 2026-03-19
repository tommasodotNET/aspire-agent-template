using Microsoft.Agents.AI;
using Microsoft.Agents.AI.DevUI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Agents.AI.Hosting.AGUI.AspNetCore;
using Microsoft.Extensions.AI;
#if (IncludeHandoff)
using Microsoft.Agents.AI.Workflows;
#endif
using MyAgentApp.Agent;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// ── Domain Services ─────────────────────────────────────────────────────────
// Register your domain services here. The AI agent calls these through tools.
// Architecture: API request → Agent → Tools → Domain Service

builder.Services.AddSingleton<TodoService>();

#if (IncludeMcp)
// ── MCP Tool Discovery ──────────────────────────────────────────────────────
// McpToolProvider connects to the MCP server at startup (via Aspire service
// discovery) and discovers available tools. The agent merges these with
// in-process tools automatically.
builder.Services.AddSingleton<McpToolProvider>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<McpToolProvider>());
#endif

// ── LLM Client (Aspire-native) ──────────────────────────────────────────────
// The OpenAI client is configured via Aspire connection string injection.
// Set the connection string in the AppHost project:
#if (UseFoundry)
//   cd MyAgentApp.AppHost
//   dotnet user-secrets set "ConnectionStrings:openai" "Endpoint=https://your-foundry-endpoint.openai.azure.com/"
#elif (UseAzureOpenAI)
//   cd MyAgentApp.AppHost
//   dotnet user-secrets set "ConnectionStrings:openai" "Endpoint=https://your-resource.openai.azure.com/"
#else
//   cd MyAgentApp.AppHost
//   dotnet user-secrets set "ConnectionStrings:openai" "Endpoint=https://api.openai.com/v1;Key=sk-your-key"
//   For GitHub Models: "Endpoint=https://models.inference.ai.azure.com;Key=ghp_your-token"
#endif

var connectionString = builder.Configuration.GetConnectionString("openai");
if (!string.IsNullOrEmpty(connectionString))
{
#if (UseOpenAI)
    // Auto-detects provider (OpenAI or Azure OpenAI) from connection string format
    builder.AddOpenAIClientFromConfiguration("openai");
#else
    builder.AddAzureOpenAIClient("openai");
#endif

    // Register the agent using the Hosting pattern so DevUI can discover it.
    var deployment = builder.Configuration["OpenAI:Deployment"] ?? "gpt-4o-mini";
#if (!IncludeHandoff)
    builder.AddAIAgent("MyAgent", (sp, name) =>
    {
        var openaiClient = sp.GetRequiredService<OpenAI.OpenAIClient>();
        var chatClient = openaiClient.GetChatClient(deployment).AsIChatClient();
        var todoTools = new TodoTools(sp.GetRequiredService<TodoService>());
        var tools = new List<AITool>(todoTools.AsAIFunctions());
#if (IncludeMcp)
        // Merge MCP tools (discovered at startup) with in-process tools.
        var mcpToolProvider = sp.GetRequiredService<McpToolProvider>();
        tools.AddRange(mcpToolProvider.Tools);
#endif
        return chatClient.AsAIAgent(
            name: name,
            instructions: """
                You are a helpful AI assistant that manages a todo list.
                Use the available tools to add, list, complete, and delete todo items.
                Be friendly, concise, and helpful. When listing todos, format them clearly.
                """,
            tools: tools);
    });
#else
    // ── Multi-Agent Handoff Workflow ─────────────────────────────────────────
    // Router: classifies user intent and routes to the appropriate specialist.
    // Specialist: handles domain-specific tasks using tools.
    // TODO: Add more specialist agents for different domains.

    builder.AddAIAgent("Router", (sp, name) =>
    {
        var openaiClient = sp.GetRequiredService<OpenAI.OpenAIClient>();
        var chatClient = openaiClient.GetChatClient(deployment).AsIChatClient();
        return chatClient.AsAIAgent(
            name: name,
            instructions: """
                You are a routing agent. Your job is to understand the user's intent
                and hand off to the right specialist agent.

                Available specialists:
                - "Specialist": Handles todo list management (add, list, complete, delete items)

                If the user's request relates to managing tasks or todos, hand off to Specialist.
                For general conversation or greetings, respond directly.

                TODO: Add more specialists here as you expand the application.
                """);
    });

    builder.AddAIAgent("Specialist", (sp, name) =>
    {
        var openaiClient = sp.GetRequiredService<OpenAI.OpenAIClient>();
        var chatClient = openaiClient.GetChatClient(deployment).AsIChatClient();
        var todoTools = new TodoTools(sp.GetRequiredService<TodoService>());
        var tools = new List<AITool>(todoTools.AsAIFunctions());
#if (IncludeMcp)
        var mcpToolProvider = sp.GetRequiredService<McpToolProvider>();
        tools.AddRange(mcpToolProvider.Tools);
#endif
        return chatClient.AsAIAgent(
            name: name,
            instructions: """
                You are a specialist agent that manages a todo list.
                Use the available tools to add, list, complete, and delete todo items.
                Be friendly, concise, and helpful. When listing todos, format them clearly.
                If the user asks about something outside your expertise, hand off back to Router.
                """,
            tools: tools);
    });

    // Build the handoff workflow — Router is the entry point.
    // The workflow is registered as "MyAgent" so AG-UI and DevUI work seamlessly.
    builder.AddWorkflow("MyAgent", (sp, key) =>
    {
        var router = sp.GetRequiredKeyedService<AIAgent>("Router");
        var specialist = sp.GetRequiredKeyedService<AIAgent>("Specialist");
        return AgentWorkflowBuilder.CreateHandoffBuilderWith(router)
            .WithHandoffs(router, [specialist])
            .WithHandoffs(specialist, [router])
            .Build();
    }).AddAsAIAgent();
#endif
}

// ── AG-UI Protocol ───────────────────────────────────────────────────────────
// AG-UI provides standardized streaming communication between the agent and
// web clients via Server-Sent Events (SSE).
builder.Services.AddAGUI();

// ── OpenAI-compatible API (required by DevUI) ───────────────────────────────
builder.Services.AddOpenAIResponses();
builder.Services.AddOpenAIConversations();

var app = builder.Build();

app.MapDefaultEndpoints();

// ── AG-UI Endpoint (streaming) ───────────────────────────────────────────────
// POST /api/agui — streams agent responses as Server-Sent Events.
// The WebUI uses AGUIChatClient to connect to this endpoint.
var agent = app.Services.GetKeyedService<AIAgent>("MyAgent");
if (agent is not null)
{
    app.MapAGUI("/api/agui", agent);
}

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

app.MapGet("/", (IServiceProvider sp) => sp.GetKeyedService<AIAgent>("MyAgent") is null
#if (UseFoundry)
    ? "⚠️ Agent Service is running but AI is not configured. Set ConnectionStrings:openai to your Foundry endpoint in AppHost user-secrets."
#elif (UseAzureOpenAI)
    ? "⚠️ Agent Service is running but AI is not configured. Set ConnectionStrings:openai to your Azure OpenAI endpoint in AppHost user-secrets."
#else
    ? "⚠️ Agent Service is running but AI is not configured. Set ConnectionStrings:openai with your OpenAI API key in AppHost user-secrets."
#endif
    : "Agent Service is running. AG-UI endpoint at /api/agui. DevUI at /devui.");

app.Run();
