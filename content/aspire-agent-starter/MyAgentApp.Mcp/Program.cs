using MyAgentApp.Mcp;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// ── MCP Server ───────────────────────────────────────────────────────────────
// Registers the MCP server with HTTP transport so Aspire can route to it.
// Tools are discovered automatically from classes marked with [McpServerToolType].
builder.Services
    .AddMcpServer()
    .WithHttpTransport()
    .WithToolsFromAssembly();

var app = builder.Build();

app.MapDefaultEndpoints();

// Map the MCP endpoint — agents connect here to discover and invoke tools.
app.MapMcp();

app.MapGet("/", () => "MCP Server is running. Connect via /mcp endpoint.");

app.Run();
