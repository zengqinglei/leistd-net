namespace Leistd.DependencyInjection.Abstractions;

/// <summary>
/// 向服务注册回调公开当前描述符的类型信息。
/// </summary>
public interface IOnServiceRegisteredContext
{
    /// <summary>
    /// 获取注册的服务类型。
    /// </summary>
    Type ServiceType { get; }

    /// <summary>
    /// 获取具体类型；工厂委托注册时为 <see langword="null"/>。
    /// </summary>
    /// <remarks>
    /// 回调对工厂委托注册同样会被调用——那类注册拿不到实现类型，但仅凭
    /// <see cref="ServiceType"/> 就能判定的约定（例如"这是不是某个已知接口"）依然成立，
    /// 而织入器本身是支持工厂型描述符的。若回调的判定确实依赖实现类型，自行对
    /// <see langword="null"/> 返回"不处理"。
    /// </remarks>
    Type? ImplementationType { get; }

    /// <summary>
    /// 获取键控注册的服务键；非键控注册时为 <see langword="null"/>。
    /// </summary>
    /// <remarks>
    /// 键控注册与同类型的非键控注册是<b>两个不同的服务</b>，回调若只按
    /// <see cref="ServiceType"/> 判定，会同时命中两者。织入器保留键控形态，
    /// 因此按服务类型下结论的约定对键控注册同样安全。
    /// </remarks>
    object? ServiceKey { get; }

    /// <summary>
    /// 扩展数据。上层集成组件可在不污染核心 DI 抽象的前提下挂载自定义信息。
    /// </summary>
    IDictionary<string, object?> Items { get; }
}
