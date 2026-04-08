using System.ComponentModel;
using Microsoft.Extensions.AI;

namespace MyAgentApp.Agent;

/// <summary>
/// Domain tools that expose TodoService operations to the AI agent.
/// This demonstrates the architecture: API → Agent → Tools → Domain Service.
///
/// Each method becomes a function tool the agent can invoke during conversations.
/// Add your own tools by creating methods with [Description] attributes and
/// registering them in <see cref="AsAIFunctions"/>.
/// </summary>
public class TodoTools(TodoService todoService)
{
    [Description("Add a new todo item to the list")]
    public string AddTodo([Description("The title of the todo item")] string title)
    {
        var item = todoService.Add(title);
        return $"Added: {item}";
    }

    [Description("List all todo items, optionally filtering to only incomplete items")]
    public string ListTodos([Description("Include completed items (default: true)")] bool includeCompleted = true)
    {
        var items = todoService.List(includeCompleted);
        if (items.Count == 0) return "No todo items found.";
        return string.Join("\n", items);
    }

    [Description("Mark a todo item as complete by its ID")]
    public string CompleteTodo([Description("The ID of the todo item to complete")] int id)
    {
        var item = todoService.Complete(id);
        return item is not null ? $"Completed: {item}" : $"Todo #{id} not found.";
    }

    [Description("Delete a todo item by its ID")]
    public string DeleteTodo([Description("The ID of the todo item to delete")] int id)
    {
        return todoService.Delete(id) ? $"Deleted todo #{id}." : $"Todo #{id} not found.";
    }

    /// <summary>
    /// Converts the tools in this class to AI function tools for agent registration.
    /// Pass the TodoTools instance so tools can access the domain service via DI.
    /// </summary>
    public IList<AITool> AsAIFunctions()
    {
        return new List<AITool>
        {
            AIFunctionFactory.Create(AddTodo, nameof(AddTodo)),
            AIFunctionFactory.Create(ListTodos, nameof(ListTodos)),
            AIFunctionFactory.Create(CompleteTodo, nameof(CompleteTodo)),
            AIFunctionFactory.Create(DeleteTodo, nameof(DeleteTodo))
        };
    }
}
