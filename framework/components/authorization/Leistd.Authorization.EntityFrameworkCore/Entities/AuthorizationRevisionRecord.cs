using Leistd.Auditing;

namespace Leistd.Authorization.EntityFrameworkCore;

/// <summary>
/// 授予主体的授权版本，每次写入递增。
/// </summary>
/// <remarks>
/// 一张表同时支撑三件事：批量替换的乐观并发（返回 409 而非静默覆盖）、客户端有效权限缓存
/// 的过期判断，以及后续引入分布式权限缓存时的失效信号。角色成员变更不需要在此扇出写入，
/// 因为主体版本由"用户版本 + 参与拼接的角色版本集合"共同构成，见
/// <see cref="SubjectPermissionGrants.Revision"/>。
/// </remarks>
public class AuthorizationRevisionRecord : IModificationAuditedObject
{
    /// <summary>版本记录 ID（有序 Guid v7）。</summary>
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>授予对象类型，如 User、Role。</summary>
    public string ProviderName { get; set; } = default!;

    /// <summary>授予对象 Key，如 UserId、RoleId。</summary>
    public string ProviderKey { get; set; } = default!;

    /// <summary>当前版本号，从 1 开始，每次授权写入递增。</summary>
    public long Version { get; set; }

    /// <inheritdoc />
    public DateTime? LastModificationTime { get; set; }

    /// <inheritdoc />
    public string? LastModifierId { get; set; }
}
