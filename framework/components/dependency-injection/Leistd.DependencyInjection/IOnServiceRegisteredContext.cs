namespace Leistd.DependencyInjection;

/// <summary>
/// 服务注册上下文接口
/// </summary>
public interface IOnServiceRegisteredContext
{
    /// <summary>
    /// 服务类型
    /// </summary>
    Type ServiceType { get; }

    /// <summary>
    /// 实现类型
    /// </summary>
    Type ImplementationType { get; }

    /// <summary>
    /// 扩展数据。上层集成组件可在不污染核心 DI 抽象的前提下挂载自定义信息。
    /// </summary>
    IDictionary<string, object?> Items { get; }
}
