namespace MyAgentApp.Agent;

/// <summary>
/// A simple in-memory domain service for managing todo items.
/// This demonstrates the pattern: Agent → Tools → Domain Service.
/// In a real application, this would use a database or external API.
/// </summary>
public class TodoService
{
    private readonly List<TodoItem> _items = [];
    private int _nextId = 1;

    public TodoItem Add(string title)
    {
        var item = new TodoItem(_nextId++, title);
        _items.Add(item);
        return item;
    }

    public IReadOnlyList<TodoItem> List(bool includeCompleted = true)
    {
        return includeCompleted ? _items.AsReadOnly() : _items.Where(t => !t.IsComplete).ToList();
    }

    public TodoItem? Complete(int id)
    {
        var item = _items.Find(t => t.Id == id);
        if (item is not null) item.IsComplete = true;
        return item;
    }

    public bool Delete(int id)
    {
        return _items.RemoveAll(t => t.Id == id) > 0;
    }

    public TodoItem? GetById(int id) => _items.Find(t => t.Id == id);
}

public class TodoItem(int id, string title)
{
    public int Id { get; } = id;
    public string Title { get; set; } = title;
    public bool IsComplete { get; set; }

    public override string ToString() =>
        $"[{(IsComplete ? "✓" : " ")}] #{Id}: {Title}";
}
