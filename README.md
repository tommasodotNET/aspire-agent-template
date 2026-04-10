# aspire-agent-template

`dotnet new` templates for building AI agent applications with [.NET Aspire](https://learn.microsoft.com/dotnet/aspire/) and the [Microsoft Agent Framework](https://learn.microsoft.com/dotnet/ai/agents).

## Templates

| Template | Short name | What you get |
|----------|-----------|--------------|
| **[Aspire AI Agent](content/aspire-agent)** | `aspire-agent` | Minimal starting point — AppHost, Agent service with DevUI, ServiceDefaults |
| **[Aspire AI Agent Starter App](content/aspire-agent-starter)** | `aspire-agent-starter` | Full app — adds Blazor chat UI, sample domain tools, optional MCP server and multi-agent handoff |

### aspire-agent (minimal)

A clean foundation for building an AI agent service. Includes the Aspire orchestrator, an agent with DevUI for testing, and shared service defaults. No UI, no sample domain logic — just the wiring.

```bash
dotnet new aspire-agent -n MyAgent
```

```
MyAgent/
├── MyAgent.AppHost/           # Aspire orchestrator
├── MyAgent.Agent/             # AI agent service with DevUI
└── MyAgent.ServiceDefaults/   # OpenTelemetry, health checks, resilience
```

### aspire-agent-starter (full app)

Everything from `aspire-agent` plus a Blazor chat UI, sample todo tools, and optional features. This is the recommended starting point for most projects.

```bash
dotnet new aspire-agent-starter -n MyAgent
```

```
MyAgent/
├── MyAgent.AppHost/           # Aspire orchestrator
├── MyAgent.Agent/             # AI agent service with AG-UI endpoint, DevUI, tools
├── MyAgent.Web/               # Blazor Server chat UI with streaming
└── MyAgent.ServiceDefaults/   # OpenTelemetry, health checks, resilience
```

**Optional features** (flags):

```bash
# Add MCP server for external tool hosting
dotnet new aspire-agent-starter -n MyAgent --IncludeMcp

# Add multi-agent handoff (Router → Specialist)
dotnet new aspire-agent-starter -n MyAgent --IncludeHandoff

# Kitchen sink — everything
dotnet new aspire-agent-starter -n MyAgent --IncludeMcp --IncludeHandoff
```

## Quick Start

### 1. Install the templates

```bash
dotnet new install path/to/aspire-agent-template/content
```

This installs both `aspire-agent` and `aspire-agent-starter`.

### 2. Create a project

```bash
dotnet new aspire-agent-starter -n MyAgent
cd MyAgent
```

### 3. Choose an AI provider

Both templates support four providers via the `--provider` flag:

| Provider | Flag | Setup |
|----------|------|-------|
| **Azure AI Foundry** (default) | `--provider Foundry` | `az login` — Aspire auto-provisions on first run |
| **Foundry Local** | `--provider FoundryLocal` | Install [Foundry Local](https://learn.microsoft.com/azure/ai-foundry/foundry-local/get-started) — no Azure needed |
| **Azure OpenAI** | `--provider AzureOpenAI` | Set connection string + `az login` |
| **OpenAI** | `--provider OpenAI` | Set connection string with API key |

Example with Foundry Local (no Azure account needed):

```bash
dotnet new aspire-agent-starter -n MyAgent --provider FoundryLocal
```

### 4. Run

```bash
cd MyAgent.AppHost
dotnet run
```

The Aspire dashboard opens automatically. For `aspire-agent`, open the Agent endpoint to access DevUI. For `aspire-agent-starter`, click the Web UI link to start chatting.

> **First run with Foundry provider:** Aspire prompts for your Azure subscription, location, and resource group, then provisions the Foundry resource (3-10 minutes). Subsequent runs start instantly.

## Changing Models

The model is declared in the AppHost's `Program.cs`. To switch models:

```csharp
var chat = foundry.AddDeployment("chat", FoundryModel.OpenAI.Gpt5Mini);  // ← change this
```

Aspire detects the change and re-provisions automatically (~30-60s on next run).

To use a pre-existing Azure resource instead of auto-provisioning:

```csharp
var foundry = builder.AddFoundry("foundry")
    .RunAsExisting("my-resource-name", "my-resource-group");
```

## Template Parameters

### aspire-agent

| Parameter | Description | Default |
|-----------|-------------|---------|
| `--provider` | AI provider (Foundry, FoundryLocal, AzureOpenAI, OpenAI) | Foundry |

### aspire-agent-starter

| Parameter | Description | Default |
|-----------|-------------|---------|
| `--provider` | AI provider (Foundry, FoundryLocal, AzureOpenAI, OpenAI) | Foundry |
| `--IncludeWeb` | Include Blazor chat frontend | true |
| `--IncludeMcp` | Include MCP server for external tools | false |
| `--IncludeHandoff` | Include multi-agent handoff workflow | false |

## Example Apps

See these projects built from the templates:

- **[InterviewCoach](https://github.com/leslierichardson95/InterviewCoach)** — Multi-agent mock interview app (`aspire-agent-starter --IncludeMcp --IncludeHandoff --provider Foundry`)

## Learn More

- [.NET Aspire documentation](https://learn.microsoft.com/dotnet/aspire/)
- [Microsoft Agent Framework](https://learn.microsoft.com/dotnet/ai/agents)
- [AG-UI Protocol](https://learn.microsoft.com/agent-framework/ag-ui/)
- [DevUI](https://learn.microsoft.com/agent-framework/devui/)
- [Microsoft.Extensions.AI](https://learn.microsoft.com/dotnet/ai/ai-extensions)
- [MCP in .NET](https://learn.microsoft.com/dotnet/ai/quickstarts/build-mcp-server)

## License

MIT
