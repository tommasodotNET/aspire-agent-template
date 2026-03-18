# XmlEncodedProjectName

An AI agent application built with [.NET Aspire](https://learn.microsoft.com/dotnet/aspire/) and the [Microsoft Agent Framework](https://learn.microsoft.com/dotnet/ai/agents).

## Architecture

```
Browser (Blazor Chat UI)
  │
  ▼
XmlEncodedProjectName.Web ──HTTP──▶ XmlEncodedProjectName.Agent
                              │
                              ▼
                          AI Agent (Azure OpenAI)
                              │
                              ▼
                          TodoTools → TodoService
```

**The flow:** User message → Web UI → Agent API → AI Agent → Tool calls → Domain Service → Response

This follows the pattern: `API request → Application service → Agent → Tools → Domain services`

## Projects

| Project | Purpose |
|---------|---------|
| **XmlEncodedProjectName.AppHost** | Aspire orchestrator — run this to start everything |
| **XmlEncodedProjectName.Agent** | AI agent API service with tools and domain logic |
| **XmlEncodedProjectName.Web** | Blazor Server chat UI |
| **XmlEncodedProjectName.ServiceDefaults** | Shared OpenTelemetry, health checks, resilience |
| **XmlEncodedProjectName.Tests** | xUnit tests for domain tools |

## Getting Started

### 1. Configure Azure OpenAI

```bash
cd XmlEncodedProjectName.Agent
dotnet user-secrets set "AzureOpenAI:Endpoint" "https://your-resource.openai.azure.com"
dotnet user-secrets set "AzureOpenAI:Deployment" "gpt-4o-mini"
```

You'll need an Azure OpenAI resource with a deployed model. The app uses `AzureCliCredential` for authentication — make sure you're logged in:

```bash
az login
```

### 2. Run the App

```bash
cd XmlEncodedProjectName.AppHost
dotnet run
```

This starts the Aspire dashboard, the Agent API, and the Web UI. Open the dashboard URL shown in the console to see all services.

### 3. Chat with the Agent

Open the Web UI link from the Aspire dashboard. Try:
- "Add a todo to buy groceries"
- "What's on my list?"
- "Mark item 1 as complete"
- "Delete item 2"

### 4. Use DevUI (Development)

When running locally, the Agent service includes **DevUI** — a built-in web interface from the Microsoft Agent Framework for debugging and testing agents.

DevUI lets you:
- **Chat directly with the agent** without the Blazor UI
- **Inspect registered tools** and their parameters
- **Trace tool calls** and agent reasoning

Access DevUI by navigating to **`/devui`** on the Agent service URL (find the Agent endpoint in the Aspire dashboard, then append `/devui`).

> **Note:** DevUI is only available in the `Development` environment. It is not mapped in production.

## How to Extend

### Add a new tool

1. Add a method to `TodoTools.cs` (or create a new tools class):
   ```csharp
   [Description("Search todos by keyword")]
   public string SearchTodos([Description("Keyword to search for")] string keyword)
   {
       var matches = todoService.List().Where(t => t.Title.Contains(keyword, StringComparison.OrdinalIgnoreCase));
       return matches.Any() ? string.Join("\n", matches) : "No matches found.";
   }
   ```

2. Register it in the `AsAIFunctions()` method:
   ```csharp
   AIFunctionFactory.Create(SearchTodos, nameof(SearchTodos))
   ```

### Swap the AI provider

The agent uses `IChatClient` from `Microsoft.Extensions.AI` — swap the implementation in `Program.cs`:

```csharp
// Azure OpenAI (default)
new AzureOpenAIClient(new Uri(endpoint), new AzureCliCredential())
    .GetChatClient(deployment).AsIChatClient();

// OpenAI
new OpenAIClient(apiKey).GetChatClient("gpt-4o-mini").AsIChatClient();

// Ollama (local)
new OllamaChatClient(new Uri("http://localhost:11434"), "llama3.2");
```

### Add a real domain service

Replace `TodoService` with your own domain (e.g., database-backed Orders, Customers):

1. Create your service class and register in DI
2. Create a tools class that wraps your service methods
3. Update the `AIAgent` registration to use your tools

## Running Tests

```bash
dotnet test
```

## Learn More

- [.NET Aspire documentation](https://learn.microsoft.com/dotnet/aspire/)
- [Microsoft Agent Framework](https://learn.microsoft.com/dotnet/ai/agents)
- [Microsoft.Extensions.AI](https://learn.microsoft.com/dotnet/ai/ai-extensions)
