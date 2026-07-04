namespace Leistd.DependencyInjection;

/// <summary>
/// 服务注册上下文实现
/// </summary>
public record class OnServiceRegisteredContext(Type ServiceType, Type ImplementationType) : IOnServiceRegisteredContext
{
    /// <summary>
    /// 扩展数据。
    /// </summary>
    public IDictionary<string, object?> Items { get; } = new Dictionary<string, object?>();
}
