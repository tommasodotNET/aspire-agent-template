#if (UseAnyFoundry)
using Aspire.Hosting.Foundry;
#endif

var builder = DistributedApplication.CreateBuilder(args);

builder.AddAzureContainerAppEnvironment("aspire-env");

// ── LLM Configuration ───────────────────────────────────────────────────────
#if (UseFoundry)
// Microsoft Foundry — model deployment declared in code, auto-provisioned by Aspire.
// No manual user-secrets needed for run mode; Aspire injects connection info automatically.
var foundry = builder.AddFoundry("foundry");
var project = foundry.AddProject("project");
var chat = project.AddModelDeployment("chat", FoundryModel.OpenAI.Gpt4oMini);
#elif (UseFoundryLocal)
// Foundry Local — runs a local LLM, no Azure account needed.
// Requires Foundry Local installed: https://learn.microsoft.com/azure/ai-foundry/foundry-local/get-started
var foundry = builder.AddFoundry("foundry")
    .RunAsFoundryLocal();
var chat = foundry.AddModelDeployment("chat", FoundryModel.Local.Phi4);
#elif (UseAzureOpenAI)
// Azure OpenAI — Aspire prompts for the connection string in the dashboard if not configured.
// Can also be set via user-secrets: ConnectionStrings:openai
var openai = builder.AddConnectionString("openai");
#else
// OpenAI API — Aspire prompts for the connection string in the dashboard if not configured.
// Can also be set via user-secrets: ConnectionStrings:openai
// For GitHub Models, use: Endpoint=https://models.inference.ai.azure.com;Key=ghp_your-token
var openai = builder.AddConnectionString("openai");
#endif

#if (IncludeMcp)
// ── MCP Server ───────────────────────────────────────────────────────────────
// The MCP server hosts domain tools accessible via the Model Context Protocol.
// The agent discovers and invokes these tools automatically at startup.
var mcp = builder.AddProject<Projects.MyAgentApp_Mcp>("mcp-server");
#endif

var agent = builder.AddProject<Projects.MyAgentApp_Agent>("agent")
#if (UseAnyFoundry)
    .WithReference(chat)
    .WaitFor(chat)
#else
    .WithReference(openai)
#endif
#if (IncludeMcp)
    .WithReference(mcp)
    .WaitFor(mcp)
#endif
    .WithUrlForEndpoint("https", url => url.Url = "/devui");

#if (IncludeWeb)
var web = builder.AddProject<Projects.MyAgentApp_Web>("web")
    .WithExternalHttpEndpoints()
    .WithReference(agent)
    .WaitFor(agent);
#endif

builder.Build().Run();
