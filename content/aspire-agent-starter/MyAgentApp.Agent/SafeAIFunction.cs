using Microsoft.Extensions.AI;

namespace MyAgentApp.Agent;

/// <summary>
/// Wraps an AIFunction to catch exceptions and return error strings
/// instead of letting exceptions propagate (which causes serialization
/// errors with System.Reflection.MethodBase on Exception.TargetSite).
/// </summary>
public class SafeAIFunction(AIFunction inner) : DelegatingAIFunction(inner)
{
    protected override async ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        try
        {
            return await base.InvokeCoreAsync(arguments, cancellationToken);
        }
        catch (Exception ex)
        {
            return $"Tool '{Name}' error: {ex.Message}";
        }
    }

    /// <summary>Wraps all tools in a list with exception handling.</summary>
    public static List<AITool> WrapAll(IEnumerable<AITool> tools) =>
        tools.Select(t => t is AIFunction fn ? new SafeAIFunction(fn) : t).ToList<AITool>();
}
