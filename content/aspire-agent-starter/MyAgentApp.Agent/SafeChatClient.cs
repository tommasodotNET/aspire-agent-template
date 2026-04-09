using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace MyAgentApp.Agent;

/// <summary>
/// Wraps an IChatClient to catch exceptions (e.g., 400 Bad Request from Azure OpenAI)
/// and return error messages instead of letting exceptions propagate through the agent
/// framework, which causes MethodBase serialization errors in DevUI.
/// </summary>
public class SafeChatClient(IChatClient inner, ILogger<SafeChatClient> logger) : DelegatingChatClient(inner)
{
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        try
        {
            return await base.GetResponseAsync(messages, options, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "LLM call failed: {Message}", ex.Message);
            return new ChatResponse([new ChatMessage(ChatRole.Assistant,
                $"I encountered an error processing your request. Please try again or clear the chat. (Error: {ex.Message})")]);
        }
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        IAsyncEnumerator<ChatResponseUpdate>? enumerator = null;
        ChatResponseUpdate? errorUpdate = null;
        bool started = false;

        try
        {
            enumerator = base.GetStreamingResponseAsync(messages, options, cancellationToken)
                .GetAsyncEnumerator(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "LLM streaming call failed: {Message}", ex.Message);
            errorUpdate = new ChatResponseUpdate(ChatRole.Assistant, $"Error: {ex.Message}");
        }

        if (errorUpdate is not null)
        {
            yield return errorUpdate;
            yield break;
        }

        while (enumerator is not null)
        {
            bool hasNext = false;
            string? midStreamError = null;
            try
            {
                hasNext = await enumerator.MoveNextAsync();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "LLM streaming failed mid-stream: {Message}", ex.Message);
                midStreamError = started ? "\n\n[Error: connection lost]" : $"Error: {ex.Message}";
                await enumerator.DisposeAsync();
                enumerator = null;
            }

            if (midStreamError is not null)
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, midStreamError);
                yield break;
            }

            if (!hasNext)
            {
                await enumerator!.DisposeAsync();
                yield break;
            }

            started = true;
            yield return enumerator!.Current;
        }
    }
}
