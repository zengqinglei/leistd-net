namespace Leistd.DependencyInjection;

/// <summary>
/// 服务注册上下文实现
/// </summary>
public record class OnServiceRegisteredContext(Type ServiceType, Type ImplementationType) : IOnServiceRegisteredContext
{
    /// <summary>
    /// 拦截器列表
    /// </summary>
    public List<Type> Interceptors { get; } = [];
}

