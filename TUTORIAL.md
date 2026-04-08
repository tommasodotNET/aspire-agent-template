# Building AI Agents with .NET Aspire — Progressive Tutorial

This tutorial walks you from a minimal AI agent to a full-featured application with tools, web UI, MCP, multi-agent handoff, and deployment.

Each part builds on the previous one. Start with Part 1 and add capabilities as needed.

---

## Part 1: Your First Agent

Create a minimal AI agent with Aspire orchestration and DevUI for testing.

### Create the project

```bash
dotnet new aspire-agent -n MyAgentApp --provider FoundryLocal
cd MyAgentApp
```

> **Provider options:** `Foundry` (Azure AI Foundry), `FoundryLocal` (local LLM, no Azure needed), `AzureOpenAI`, `OpenAI`

### What you get

```
MyAgentApp/
├── MyAgentApp.AppHost/          ← Aspire orchestrator (run this)
├── MyAgentApp.Agent/            ← AI agent service with DevUI
├── MyAgentApp.ServiceDefaults/  ← Shared telemetry, health checks
└── MyAgentApp.slnx
```

Three projects. The AppHost starts everything; the Agent talks to the LLM.

### Run it

```bash
cd MyAgentApp.AppHost
dotnet run
```

Open the Aspire dashboard URL from the console. Click the agent's endpoint to open **DevUI** — a built-in chat interface for testing your agent.

### What's happening

```
AppHost (orchestrator)
   └── Agent (web service)
         ├── LLM connection (Foundry/OpenAI)
         ├── OpenAI Responses API (/v1/responses)
         └── DevUI (/devui)
```

- **AppHost** starts the Agent and injects the LLM connection string
- **Agent** registers an `AIAgent` with a system prompt and connects to the LLM
- **DevUI** provides a chat UI for development — no frontend needed yet

### Key file: Agent/Program.cs

```csharp
builder.AddAIAgent("MyAgent", (sp, name) =>
{
    var chatClient = sp.GetRequiredService<IChatClient>();
    return chatClient.AsAIAgent(
        name: name,
        instructions: "You are a helpful AI assistant.");
});
```

This is the core pattern: register an `AIAgent` with a chat client and instructions.

---

## Part 2: Adding Domain Tools

Give your agent the ability to *do things* by adding tool functions.

### Create a domain service

Add a new file `Agent/WeatherService.cs`:

```csharp
namespace MyAgentApp.Agent;

public class WeatherService
{
    private readonly Dictionary<string, string> _forecasts = new()
    {
        ["Seattle"] = "62°F, cloudy",
        ["New York"] = "78°F, sunny",
        ["London"] = "55°F, rain"
    };

    public string GetForecast(string city) =>
        _forecasts.TryGetValue(city, out var forecast)
            ? $"{city}: {forecast}"
            : $"No forecast available for {city}";

    public IEnumerable<string> GetCities() => _forecasts.Keys;
}
```

### Create tool functions

Add `Agent/WeatherTools.cs`:

```csharp
using System.ComponentModel;
using Microsoft.Extensions.AI;

namespace MyAgentApp.Agent;

public class WeatherTools(WeatherService weatherService)
{
    [Description("Get the weather forecast for a city")]
    public string GetWeather([Description("City name")] string city)
        => weatherService.GetForecast(city);

    [Description("List all cities with available forecasts")]
    public string ListCities()
        => string.Join(", ", weatherService.GetCities());

    public IList<AIFunction> AsAIFunctions() =>
    [
        AIFunctionFactory.Create(GetWeather, nameof(GetWeather)),
        AIFunctionFactory.Create(ListCities, nameof(ListCities))
    ];
}
```

### Wire into the agent

Update `Agent/Program.cs`:

```csharp
using MyAgentApp.Agent;

// ... after builder.AddServiceDefaults()

builder.Services.AddSingleton<WeatherService>();

// ... in the AddAIAgent callback:
builder.AddAIAgent("MyAgent", (sp, name) =>
{
    var chatClient = sp.GetRequiredService<IChatClient>();
    var weatherTools = new WeatherTools(sp.GetRequiredService<WeatherService>());
    var tools = new List<AITool>(weatherTools.AsAIFunctions());

    return chatClient.AsAIAgent(
        name: name,
        instructions: """
            You are a weather assistant. Use the available tools to look up
            weather forecasts. Be friendly and concise.
            """,
        tools: tools);
});
```

### Test it

Run the AppHost and open DevUI. Try:
- "What's the weather in Seattle?"
- "What cities can you check?"

The agent will call your tool functions and use the results in its response.

### Key concept

Tools are just C# methods with `[Description]` attributes. The `AsAIFunctions()` pattern converts them to `AITool` instances that the LLM can invoke. The LLM sees the descriptions and parameter types to decide when/how to call them.

---

## Part 3: Adding a Web Frontend

Give users a real chat interface instead of DevUI.

### Option A: Start with the starter template

If starting fresh, the starter template includes a Blazor web frontend:

```bash
dotnet new aspire-agent-starter -n MyAgentApp --provider Foundry
```

### Option B: Add web to an existing project

1. Add a Blazor Server project to your solution
2. Register an `HttpClient` pointing at the agent via Aspire service discovery:
   ```csharp
   builder.Services.AddHttpClient("AgentApi", client =>
   {
       client.BaseAddress = new Uri("https+http://agent");
   });
   ```
3. In your chat component, POST conversation history to `/v1/responses` (OpenAI Responses API) and parse the SSE stream:
   ```csharp
   var json = JsonSerializer.Serialize(new {
       model = "MyAgent",
       input = messages.Select(m => new { type = "message", role = m.Role, content = m.Content }),
       stream = true
   });
   var httpClient = HttpClientFactory.CreateClient("AgentApi");
   using var request = new HttpRequestMessage(HttpMethod.Post, "v1/responses");
   request.Content = new StringContent(json, Encoding.UTF8, "application/json");
   request.Headers.Accept.Add(new("text/event-stream"));
   using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
   // Read SSE lines, parse "data: " events with type "response.output_text.delta"
   ```

No special client package is needed — it's raw `HttpClient` + SSE parsing (~40 lines). See the starter template's `Home.razor` for the full implementation.

### How it connects

```
Browser → Blazor Web App → OpenAI Responses API (SSE) → Agent → LLM
```

- The agent exposes an **OpenAI-compatible Responses API** at `/v1/responses`
- The Web app streams deltas via `response.output_text.delta` events
- Aspire service discovery handles URL resolution — no hardcoded URLs

### AppHost wiring

```csharp
var web = builder.AddProject<Projects.MyAgentApp_Web>("web")
    .WithExternalHttpEndpoints()
    .WithReference(agent)
    .WaitFor(agent);
```

---

## Part 4: External Tools with MCP

Host tools in a separate process using the Model Context Protocol.

### When to use MCP vs in-process tools

| | In-process tools | MCP tools |
|---|---|---|
| **Latency** | Fastest (direct method call) | Slightly slower (HTTP) |
| **Isolation** | Shares agent process | Separate process |
| **Reuse** | Only this agent | Any MCP client |
| **Use case** | Core domain logic | Shared tools, third-party integrations |

### Create with the starter template

```bash
dotnet new aspire-agent-starter -n MyAgentApp --IncludeMcp
```

This adds `MyAgentApp.Mcp` — a standalone MCP server using `ModelContextProtocol.AspNetCore`.

### Add an MCP tool

In `Mcp/SampleTools.cs`:

```csharp
[McpServerToolType]
public static class SampleTools
{
    [McpServerTool, Description("Gets the current time in a timezone")]
    public static string GetTime([Description("IANA timezone")] string timezone)
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById(timezone);
        return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz).ToString("f");
    }
}
```

### How discovery works

1. The MCP server starts as a separate Aspire project
2. At agent startup, `McpToolProvider` connects via Aspire service discovery
3. Tools are discovered via `ListToolsAsync()` and merged with in-process tools
4. The agent can invoke both types transparently

---

## Part 5: Multi-Agent Handoff

Route conversations between specialist agents based on user intent.

### Create with the starter template

```bash
dotnet new aspire-agent-starter -n MyAgentApp --IncludeHandoff
```

### The pattern

```
User → Router Agent → Specialist Agent(s) → Tools → Response
```

- **Router**: Understands user intent, has NO tools (saves tokens)
- **Specialist**: Handles a specific domain, has ONLY its relevant tools

### Token optimization

Every tool schema adds ~200-400 tokens per LLM call. In a handoff chain:
- Bad: Every agent gets all tools (5 agents × 10 tools = massive token waste)
- Good: Router has 0 tools, each specialist has only 2-3

### Adding a specialist

1. Register a new agent:
```csharp
builder.AddAIAgent("Analytics", (sp, name) =>
{
    var chatClient = sp.GetRequiredService<IChatClient>();
    return chatClient.AsAIAgent(
        name: name,
        instructions: "You analyze data trends...",
        tools: analyticsTools);
});
```

2. Add to the handoff workflow:
```csharp
AgentWorkflowBuilder.CreateHandoffBuilderWith(router)
    .WithHandoffs(router, [specialist, analytics])
    .WithHandoffs(specialist, [router])
    .WithHandoffs(analytics, [router])
    .Build();
```

3. Update the Router's instructions to know about the new specialist.

### Capacity planning

Multi-agent handoff requires 2+ LLM calls per user message (router + specialist). Budget 50K+ TPM for smooth interactive use.

---

## Part 6: Testing Your Tools

Add automated tests for your tool functions.

### Create a test project

```bash
dotnet new xunit -n MyAgentApp.Tests
dotnet sln add MyAgentApp.Tests
cd MyAgentApp.Tests
dotnet add reference ../MyAgentApp.Agent
```

### Unit test tools (no LLM needed)

```csharp
using MyAgentApp.Agent;
using Xunit;

public class WeatherToolsTests
{
    private readonly WeatherService _service = new();
    private readonly WeatherTools _tools;

    public WeatherToolsTests()
    {
        _tools = new WeatherTools(_service);
    }

    [Fact]
    public void GetWeather_KnownCity_ReturnsForecast()
    {
        var result = _tools.GetWeather("Seattle");
        Assert.Contains("Seattle", result);
        Assert.Contains("°F", result);
    }

    [Fact]
    public void GetWeather_UnknownCity_ReturnsNotAvailable()
    {
        var result = _tools.GetWeather("Mars");
        Assert.Contains("No forecast", result);
    }

    [Fact]
    public void AsAIFunctions_ReturnsExpectedToolCount()
    {
        var tools = _tools.AsAIFunctions();
        Assert.Equal(2, tools.Count);
    }
}
```

### Key insight

Test the **tools**, not the LLM. Tools are deterministic C# methods — they don't need an AI model to test. This gives you fast, reliable tests that catch logic bugs without LLM costs or flakiness.

---

## Part 7: Deploying to Azure

Deploy your agent application to Azure Container Apps with `azd`.

### Prerequisites

- [Azure Developer CLI (azd)](https://learn.microsoft.com/azure/developer/azure-developer-cli/install-azd)
- An Azure subscription
- `az login` completed

### Deploy

```bash
azd init
azd up
```

`azd` will:
1. Detect the Aspire AppHost
2. Provision Azure Container Apps for each project
3. If using `AddFoundry`, auto-provision the AI Foundry resource
4. Inject connection strings and service discovery URLs

### What maps where

| Aspire project | Azure resource |
|---|---|
| Agent | Azure Container App |
| Web | Azure Container App (with external ingress) |
| MCP | Azure Container App |
| Foundry | Azure AI Foundry account + deployment |

### Production considerations

- **Authentication**: Add auth middleware (the template doesn't include auth)
- **Database**: Replace in-memory services with a real database (e.g., Azure Cosmos DB, PostgreSQL)
- **Monitoring**: OpenTelemetry is already wired — connect to Azure Monitor / Application Insights
- **Scaling**: Configure Container App scaling rules based on HTTP traffic

---

## Quick Reference

### Template commands

```bash
# Minimal agent (3 projects)
dotnet new aspire-agent -n MyApp

# Full app with web UI (4 projects)
dotnet new aspire-agent-starter -n MyApp

# Full app with MCP + Handoff (5 projects)
dotnet new aspire-agent-starter -n MyApp --IncludeMcp --IncludeHandoff

# No web frontend (use DevUI only)
dotnet new aspire-agent-starter -n MyApp --IncludeWeb false

# Local LLM (no Azure needed)
dotnet new aspire-agent -n MyApp --provider FoundryLocal
```

### Provider comparison

| Provider | Setup | Cost | Best for |
|---|---|---|---|
| `FoundryLocal` | Install Foundry Local | Free | Learning, offline dev |
| `Foundry` | Azure subscription + `az login` | Pay-per-use | Production, auto-provisioning |
| `AzureOpenAI` | Azure OpenAI resource + connection string | Pay-per-use | Existing Azure OpenAI users |
| `OpenAI` | API key | Pay-per-use | OpenAI API or GitHub Models |

## Learn More

- [.NET Aspire documentation](https://learn.microsoft.com/dotnet/aspire/)
- [Microsoft Agent Framework](https://learn.microsoft.com/dotnet/ai/agents)
- [AG-UI Protocol](https://learn.microsoft.com/agent-framework/ag-ui/)
- [DevUI](https://learn.microsoft.com/agent-framework/devui/)
- [MCP in .NET](https://learn.microsoft.com/dotnet/ai/quickstarts/build-mcp-server)
- [Microsoft.Extensions.AI](https://learn.microsoft.com/dotnet/ai/ai-extensions)
