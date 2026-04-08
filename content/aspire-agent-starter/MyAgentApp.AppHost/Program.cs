#if (UseAnyFoundry)
using Aspire.Hosting.Foundry;
#endif

var builder = DistributedApplication.CreateBuilder(args);

// ── LLM Configuration ───────────────────────────────────────────────────────
#if (UseFoundry)
// Azure AI Foundry — model deployment declared in code, auto-provisioned with 'azd up'.
// No manual user-secrets needed for run mode; Aspire injects connection info automatically.
var foundry = builder.AddFoundry("foundry");
var chat = foundry.AddDeployment("chat", FoundryModel.OpenAI.Gpt4oMini);
#elif (UseFoundryLocal)
// Foundry Local — runs a local LLM, no Azure account needed.
// Requires Foundry Local installed: https://learn.microsoft.com/azure/ai-foundry/foundry-local/get-started
var foundry = builder.AddFoundry("foundry")
    .RunAsFoundryLocal();
var chat = foundry.AddDeployment("chat", FoundryModel.Local.Phi4);
#elif (UseAzureOpenAI)
// Azure OpenAI connection string. Set in user-secrets:
//   cd MyAgentApp.AppHost
//   dotnet user-secrets set "ConnectionStrings:openai" "Endpoint=https://your-resource.openai.azure.com/"
var openai = builder.AddConnectionString("openai");
#else
// OpenAI API connection string. Set in user-secrets:
//   cd MyAgentApp.AppHost
//   dotnet user-secrets set "ConnectionStrings:openai" "Endpoint=https://api.openai.com/v1;Key=sk-your-key"
//   For GitHub Models: "Endpoint=https://models.inference.ai.azure.com;Key=ghp_your-token"
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
#endif
    .WithUrlForEndpoint("https", url => url.Url = "/devui");

#if (IncludeWeb)
var web = builder.AddProject<Projects.MyAgentApp_Web>("web")
    .WithExternalHttpEndpoints()
    .WithReference(agent)
    .WaitFor(agent);
#endif

builder.Build().Run();
