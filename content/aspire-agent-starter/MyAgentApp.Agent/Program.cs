using A2A;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.DevUI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Agents.AI.Hosting.A2A;
using Microsoft.Agents.AI.Hosting.AGUI.AspNetCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
#if (IncludeHandoff)
using Microsoft.Agents.AI.Workflows;
#endif
using MyAgentApp.Agent;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// ── Domain Services ─────────────────────────────────────────────────────────
// Register your domain services here. The AI agent calls these through tools.

// ┌─────────────────────────────────────────────────────────────────────────┐
// │ REPLACE: Swap TodoService with your own domain service.                │
// │ This is a sample in-memory todo list — replace it with your business   │
// │ logic (e.g., order management, document processing, data queries).     │
// └─────────────────────────────────────────────────────────────────────────┘
builder.Services.AddSingleton<TodoService>();

#if (IncludeMcp)
// ── MCP Tool Discovery ──────────────────────────────────────────────────────
builder.Services.AddSingleton<McpToolProvider>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<McpToolProvider>());
#endif

// ── LLM Client (Aspire-native) ──────────────────────────────────────────────
#if (UseAnyFoundry)
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
#if (!IncludeHandoff)
    builder.AddAIAgent("MyAgent", (sp, name) =>
    {
        var chatClient = new SafeChatClient(sp.GetRequiredService<IChatClient>(), sp.GetRequiredService<ILogger<SafeChatClient>>());
        // ┌─────────────────────────────────────────────────────────────────┐
        // │ REPLACE: Swap TodoTools with your own AI tool functions.        │
        // └─────────────────────────────────────────────────────────────────┘
        var todoTools = new TodoTools(sp.GetRequiredService<TodoService>());
        var tools = new List<AITool>(todoTools.AsAIFunctions());
#if (IncludeMcp)
        var mcpToolProvider = sp.GetRequiredService<McpToolProvider>();
        tools.AddRange(SafeAIFunction.WrapAll(mcpToolProvider.Tools));
#endif
        // ┌─────────────────────────────────────────────────────────────────┐
        // │ REPLACE: Update the instructions to describe your agent's role. │
        // └─────────────────────────────────────────────────────────────────┘
        return chatClient.AsAIAgent(
            name: name,
            description: "A helpful AI assistant that manages a todo list using available tools.",
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
    //
    // TOKEN OPTIMIZATION: The Router agent has NO tools — it only routes.
    // When adding more specialists, give each agent ONLY the tools it needs.
    // Every tool schema adds ~200-400 tokens per LLM call. With 5+ agents
    // in a handoff chain, unnecessary tools multiply quickly.
    //
    // CAPACITY: Multi-agent handoff requires 2+ LLM calls per user message
    // (router + specialist). Budget 50K+ TPM for smooth interactive use.

    builder.AddAIAgent("Router", (sp, name) =>
    {
        var chatClient = new SafeChatClient(sp.GetRequiredService<IChatClient>(), sp.GetRequiredService<ILogger<SafeChatClient>>());
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
        var chatClient = new SafeChatClient(sp.GetRequiredService<IChatClient>(), sp.GetRequiredService<ILogger<SafeChatClient>>());
        // ┌─────────────────────────────────────────────────────────────────┐
        // │ REPLACE: Swap TodoTools with your specialist's domain tools.    │
        // └─────────────────────────────────────────────────────────────────┘
        var todoTools = new TodoTools(sp.GetRequiredService<TodoService>());
        var tools = new List<AITool>(todoTools.AsAIFunctions());
#if (IncludeMcp)
        var mcpToolProvider = sp.GetRequiredService<McpToolProvider>();
        tools.AddRange(SafeAIFunction.WrapAll(mcpToolProvider.Tools));
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

    builder.Services.AddKeyedSingleton<Workflow>("MyAgent", (sp, key) =>
    {
        var router = sp.GetRequiredKeyedService<AIAgent>("Router");
        var specialist = sp.GetRequiredKeyedService<AIAgent>("Specialist");
        return AgentWorkflowBuilder.CreateHandoffBuilderWith(router)
            .WithHandoffs(router, [specialist])
            .WithHandoffs(specialist, [router])
            .Build();
    });

    builder.AddAIAgent("MyAgent", (sp, key) =>
        sp.GetRequiredKeyedService<Workflow>("MyAgent").AsAIAgent(
            name: key,
            description: "A multi-agent workflow that routes requests to specialist agents for task management."));
#endif
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
#if (!IncludeHandoff)
    builder.AddAIAgent("MyAgent", (sp, name) =>
    {
        var openaiClient = sp.GetRequiredService<OpenAI.OpenAIClient>();
        var chatClient = new SafeChatClient(openaiClient.GetChatClient(deployment).AsIChatClient(), sp.GetRequiredService<ILogger<SafeChatClient>>());
        // ┌─────────────────────────────────────────────────────────────────┐
        // │ REPLACE: Swap TodoTools with your own AI tool functions.        │
        // └─────────────────────────────────────────────────────────────────┘
        var todoTools = new TodoTools(sp.GetRequiredService<TodoService>());
        var tools = new List<AITool>(todoTools.AsAIFunctions());
#if (IncludeMcp)
        var mcpToolProvider = sp.GetRequiredService<McpToolProvider>();
        tools.AddRange(SafeAIFunction.WrapAll(mcpToolProvider.Tools));
#endif
        // ┌─────────────────────────────────────────────────────────────────┐
        // │ REPLACE: Update the instructions to describe your agent's role. │
        // └─────────────────────────────────────────────────────────────────┘
        return chatClient.AsAIAgent(
            name: name,
            description: "A helpful AI assistant that manages a todo list using available tools.",
            instructions: """
                You are a helpful AI assistant that manages a todo list.
                Use the available tools to add, list, complete, and delete todo items.
                Be friendly, concise, and helpful. When listing todos, format them clearly.
                """,
            tools: tools);
    });
#else
    // ── Multi-Agent Handoff Workflow ─────────────────────────────────────────
    builder.AddAIAgent("Router", (sp, name) =>
    {
        var openaiClient = sp.GetRequiredService<OpenAI.OpenAIClient>();
        var chatClient = new SafeChatClient(openaiClient.GetChatClient(deployment).AsIChatClient(), sp.GetRequiredService<ILogger<SafeChatClient>>());
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
        var chatClient = new SafeChatClient(openaiClient.GetChatClient(deployment).AsIChatClient(), sp.GetRequiredService<ILogger<SafeChatClient>>());
        // ┌─────────────────────────────────────────────────────────────────┐
        // │ REPLACE: Swap TodoTools with your specialist's domain tools.    │
        // └─────────────────────────────────────────────────────────────────┘
        var todoTools = new TodoTools(sp.GetRequiredService<TodoService>());
        var tools = new List<AITool>(todoTools.AsAIFunctions());
#if (IncludeMcp)
        var mcpToolProvider = sp.GetRequiredService<McpToolProvider>();
        tools.AddRange(SafeAIFunction.WrapAll(mcpToolProvider.Tools));
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

    builder.Services.AddKeyedSingleton<Workflow>("MyAgent", (sp, key) =>
    {
        var router = sp.GetRequiredKeyedService<AIAgent>("Router");
        var specialist = sp.GetRequiredKeyedService<AIAgent>("Specialist");
        return AgentWorkflowBuilder.CreateHandoffBuilderWith(router)
            .WithHandoffs(router, [specialist])
            .WithHandoffs(specialist, [router])
            .Build();
    });

    builder.AddAIAgent("MyAgent", (sp, key) =>
        sp.GetRequiredKeyedService<Workflow>("MyAgent").AsAIAgent(
            name: key,
            description: "A multi-agent workflow that routes requests to specialist agents for task management."));
#endif
}
#endif

// ── AG-UI Protocol ───────────────────────────────────────────────────────────
builder.Services.AddAGUI();

// ── OpenAI-compatible API (required by DevUI) ───────────────────────────────
builder.Services.AddOpenAIResponses();
builder.Services.AddOpenAIConversations();

var app = builder.Build();

#if (IncludeMcp)
// Start MCP tool discovery before MapDevUI (which eagerly resolves agents).
// This avoids deadlock from .GetAwaiter().GetResult() in agent factories.
var mcpProvider = app.Services.GetRequiredService<McpToolProvider>();
using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
{
    await mcpProvider.StartAsync(cts.Token);
}
#endif

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
    : "Agent Service is running. AG-UI endpoint at /api/agui. DevUI at /devui.");

app.Run();
