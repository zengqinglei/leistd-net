namespace Leistd.Authorization.Abstractions;

/// <summary>
/// 按授予对象查找主体：确认它存在，并给出显示名。由宿主按自己的用户、角色模型实现。
/// </summary>
/// <remarks>
/// 权限组件不认识宿主的用户与角色实体，管理用例只经这个窄钩子问两件事：
/// 给不存在的主体写授予会留下孤儿行，而审计记录里的目标若只是裸 Key，最该被看懂的记录最难看懂。
/// </remarks>
/// <example>
/// <code>
/// internal sealed class RoleSubjectDirectory(IRepository&lt;Role, Guid&gt; roles) : IPermissionSubjectDirectory
/// {
///     public async Task&lt;PermissionSubjectInfo?&gt; FindAsync(string providerName, string providerKey, CancellationToken ct = default)
///         =&gt; providerName == PermissionGrantProviderNames.Role &amp;&amp; Guid.TryParse(providerKey, out var id)
///            &amp;&amp; await roles.GetByIdAsync(id, ct) is { } role
///             ? new PermissionSubjectInfo(role.DisplayName ?? role.Name)
///             : null;
/// }
/// </code>
/// </example>
public interface IPermissionSubjectDirectory
{
    /// <summary>查找主体；Key 格式不对或主体不存在时返回 <see langword="null"/>。</summary>
    /// <param name="providerName">授予对象类型，见 <see cref="Constants.PermissionGrantProviderNames"/>。</param>
    /// <param name="providerKey">授予对象 Key。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<PermissionSubjectInfo?> FindAsync(
        string providerName,
        string providerKey,
        CancellationToken cancellationToken = default);
}

/// <summary>授予主体的展示信息。</summary>
/// <param name="DisplayName">显示名；没有时为 <see langword="null"/>。</param>
public sealed record PermissionSubjectInfo(string? DisplayName);
