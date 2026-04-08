using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;

namespace MyAgentApp.Agent;

/// <summary>
/// Discovers tools from the MCP server at startup and makes them available to the agent.
/// Uses Aspire service discovery to locate the MCP server automatically.
/// </summary>
public class McpToolProvider : IHostedService, IAsyncDisposable
{
    private readonly IConfiguration _config;
    private readonly ILogger<McpToolProvider> _logger;
    private McpClient? _mcpClient;

    public IReadOnlyList<AITool> Tools { get; private set; } = [];

    public McpToolProvider(IConfiguration config, ILogger<McpToolProvider> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Aspire injects the MCP server URL via service discovery.
        var mcpServerUrl = _config["services:mcp-server:https:0"]
            ?? _config["services:mcp-server:http:0"];

        if (string.IsNullOrEmpty(mcpServerUrl))
        {
            _logger.LogWarning("MCP server URL not found. MCP tools will not be available.");
            return;
        }

        try
        {
            _mcpClient = await McpClient.CreateAsync(
                new HttpClientTransport(new HttpClientTransportOptions
                {
                    Endpoint = new Uri(mcpServerUrl),
                    Name = "mcp-server"
                }),
                cancellationToken: cancellationToken);

            var mcpTools = await _mcpClient.ListToolsAsync(cancellationToken: cancellationToken);
            Tools = mcpTools.Cast<AITool>().ToList();
            _logger.LogInformation("Discovered {Count} MCP tools from {Url}", Tools.Count, mcpServerUrl);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to connect to MCP server. Agent will use in-process tools only.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => DisposeAsync().AsTask();

    public async ValueTask DisposeAsync()
    {
        if (_mcpClient is not null)
        {
            await _mcpClient.DisposeAsync();
            _mcpClient = null;
        }
    }
}
