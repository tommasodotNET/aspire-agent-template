using System.ComponentModel;
using ModelContextProtocol.Server;

namespace MyAgentApp.Mcp;

/// <summary>
/// Sample MCP tools that demonstrate the [McpServerTool] pattern.
/// Replace these with your domain-specific tools.
///
/// Each method decorated with [McpServerTool] is automatically discovered
/// and exposed via the MCP protocol. The agent connects to this server
/// and can invoke these tools during conversations.
/// </summary>
[McpServerToolType]
public class SampleTools
{
    [McpServerTool, Description("Gets the current date and time in UTC")]
    public static string GetCurrentTime()
    {
        return DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC");
    }

    [McpServerTool, Description("Echoes back the provided message — useful for testing MCP connectivity")]
    public static string Echo([Description("The message to echo back")] string message)
    {
        return $"Echo: {message}";
    }
}
