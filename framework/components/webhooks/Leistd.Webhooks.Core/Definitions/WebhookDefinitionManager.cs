namespace Leistd.Webhooks.Definitions;

internal sealed class WebhookDefinitionContext : IWebhookDefinitionContext
{
    private readonly Dictionary<string, WebhookDefinition> _byName = new(StringComparer.Ordinal);
    private readonly List<WebhookDefinition> _ordered = [];

    public WebhookDefinition Add(string name, string group, string? description = null)
    {
        var definition = new WebhookDefinition(name, group, description);
        if (!_byName.TryAdd(definition.Name, definition))
        {
            throw new InvalidOperationException(
                $"Webhook event type '{definition.Name}' already exists; event type names must be globally unique.");
        }

        _ordered.Add(definition);
        return definition;
    }

    public WebhookDefinition? GetOrNull(string name)
        => !string.IsNullOrWhiteSpace(name) && _byName.TryGetValue(name, out var definition) ? definition : null;

    public IReadOnlyList<WebhookDefinition> GetAll() => _ordered;
}

// 单例，构造时一次性求值：重复的类型名在启动期抛出。
internal sealed class WebhookDefinitionManager : IWebhookDefinitionManager
{
    private readonly WebhookDefinitionContext _context = new();

    public WebhookDefinitionManager(IEnumerable<IWebhookDefinitionProvider> providers)
    {
        _context.Add(WebhookDefinition.TestEventType, WebhookDefinition.TestEventGroup);
        foreach (var provider in providers)
        {
            provider.Define(_context);
        }
    }

    public WebhookDefinition? GetOrNull(string name) => _context.GetOrNull(name);

    public IReadOnlyList<WebhookDefinition> GetAll() => _context.GetAll();
}
