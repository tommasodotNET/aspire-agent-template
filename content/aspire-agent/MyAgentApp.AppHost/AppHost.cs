#if (UseAnyFoundry)
using Aspire.Hosting.Foundry;
#endif

var builder = DistributedApplication.CreateBuilder(args);

builder.AddAzureContainerAppEnvironment("aspire-env");

// ── LLM Configuration ───────────────────────────────────────────────────────
#if (UseFoundry)
// Microsoft Foundry — model deployment declared in code, auto-provisioned by Aspire.
var foundry = builder.AddFoundry("foundry");
var project = foundry.AddProject("project");
var chat = project.AddModelDeployment("chat", FoundryModel.OpenAI.Gpt4oMini);
#elif (UseFoundryLocal)
// Foundry Local — runs a local LLM, no Azure account needed.
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

var agent = builder.AddProject<Projects.MyAgentApp_Agent>("agent")
#if (UseAnyFoundry)
    .WithReference(chat)
    .WaitFor(chat)
#else
    .WithReference(openai)
#endif
    .WithUrlForEndpoint("https", url => url.Url = "/devui");

builder.Build().Run();
