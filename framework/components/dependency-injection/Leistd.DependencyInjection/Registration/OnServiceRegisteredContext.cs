using Leistd.DependencyInjection.Abstractions;

namespace Leistd.DependencyInjection.Registration;

/// <inheritdoc/>
public record class OnServiceRegisteredContext(
    Type ServiceType,
    Type? ImplementationType,
    object? ServiceKey = null) : IOnServiceRegisteredContext
{
    /// <summary>
    /// 扩展数据。
    /// </summary>
    public IDictionary<string, object?> Items { get; } = new Dictionary<string, object?>();
}
