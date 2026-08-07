namespace Leistd.Authorization.Resource;

/// <summary>
/// 内置资源操作名称。业务可以自行扩展任意字符串操作，本类只是常用值的约定。
/// </summary>
public static class ResourceOperations
{
    /// <summary>查看单个资源实例。</summary>
    public const string Read = "Read";

    /// <summary>修改资源实例。</summary>
    public const string Update = "Update";

    /// <summary>删除资源实例。</summary>
    public const string Delete = "Delete";

    /// <summary>把资源实例分享给其他主体。</summary>
    public const string Share = "Share";
}

/// <summary>
/// 可被资源实例授权保护的资源。
/// </summary>
/// <remarks>
/// 实现它可以让调用方省去手工传 <c>resourceName</c> 与 <c>resourceKey</c>。
/// 资源名建议使用稳定的复数业务名（如 <c>Orders</c>），资源 Key 使用主键的字符串形式。
/// </remarks>
public interface IAuthorizableResource
{
    /// <summary>资源类型名称，同一类资源在 ACL 中共享该名称。</summary>
    string ResourceName { get; }

    /// <summary>资源实例 Key，在同一资源类型内唯一。</summary>
    string ResourceKey { get; }
}

/// <summary>
/// 资源实例授权的判定结果。
/// </summary>
public enum ResourceAuthorizationDecision
{
    /// <summary>没有任何规则或 ACL 给出结论，最终按默认拒绝处理。</summary>
    Undefined = 0,

    /// <summary>允许。</summary>
    Allowed = 1,

    /// <summary>拒绝。优先于任何来源的允许。</summary>
    Denied = 2
}

/// <summary>
/// 资源实例授权上下文：一次判定所需的全部输入与判定结果的收集器。
/// </summary>
/// <typeparam name="TResource">已加载的资源实例类型。</typeparam>
/// <remarks>
/// 资源必须先被加载再判定，因此本上下文总是携带真实实例，
/// 规则 Handler 可以直接读取所有者、状态、成员关系等属性。
/// </remarks>
public sealed class ResourceAuthorizationContext<TResource>(
    PermissionSubject subject,
    string resourceName,
    string resourceKey,
    string operation,
    TResource resource)
{
    /// <summary>当前主体。</summary>
    public PermissionSubject Subject { get; } = subject;

    /// <summary>资源类型名称。</summary>
    public string ResourceName { get; } = resourceName;

    /// <summary>资源实例 Key。</summary>
    public string ResourceKey { get; } = resourceKey;

    /// <summary>本次判定的操作。</summary>
    public string Operation { get; } = operation;

    /// <summary>已加载的资源实例。</summary>
    public TResource Resource { get; } = resource;

    /// <summary>当前判定结果。</summary>
    public ResourceAuthorizationDecision Decision { get; private set; } = ResourceAuthorizationDecision.Undefined;

    /// <summary>
    /// 表示允许。已经被拒绝时不再生效——拒绝优先。
    /// </summary>
    public void Allow()
    {
        if (Decision != ResourceAuthorizationDecision.Denied)
        {
            Decision = ResourceAuthorizationDecision.Allowed;
        }
    }

    /// <summary>
    /// 表示拒绝。一旦被拒绝，后续任何允许都不会覆盖它。
    /// </summary>
    public void Deny() => Decision = ResourceAuthorizationDecision.Denied;
}

/// <summary>
/// 资源实例授权规则处理器：表达所有者、成员关系、资源状态、时间窗口等领域规则。
/// </summary>
/// <typeparam name="TResource">资源实例类型。</typeparam>
/// <remarks>
/// 同一资源类型可以注册多个处理器，它们全部执行；任一处理器调用
/// <see cref="ResourceAuthorizationContext{TResource}.Deny"/> 即最终拒绝。
/// 不给出结论的处理器什么都不调用即可。
/// </remarks>
public interface IResourceAuthorizationHandler<TResource>
{
    /// <summary>
    /// 对已加载的资源实例执行规则判定。
    /// </summary>
    ValueTask HandleAsync(
        ResourceAuthorizationContext<TResource> context,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 资源实例授权服务：组合规则处理器与资源 ACL，得出对单个实例的最终裁决。
/// </summary>
/// <remarks>
/// 判定顺序为：任一来源拒绝即拒绝；否则任一来源允许即允许；全部无结论时默认拒绝。
/// 本服务只负责"这个已经定位到的实例能否操作"，不负责"哪些实例可见"——
/// 后者属于集合级数据范围，见 <c>Leistd.Authorization.DataScope.Core</c>。
/// </remarks>
public interface IResourceAuthorizationService
{
    /// <summary>
    /// 判定当前主体能否对指定资源实例执行指定操作。
    /// </summary>
    Task<bool> IsGrantedAsync<TResource>(
        TResource resource,
        string resourceName,
        string resourceKey,
        string operation,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 判定当前主体能否对实现了 <see cref="IAuthorizableResource"/> 的资源执行指定操作。
    /// </summary>
    Task<bool> IsGrantedAsync<TResource>(
        TResource resource,
        string operation,
        CancellationToken cancellationToken = default)
        where TResource : IAuthorizableResource;
}
