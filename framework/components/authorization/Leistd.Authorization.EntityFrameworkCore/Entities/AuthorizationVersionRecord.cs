using Leistd.Auditing;
using Leistd.MultiTenancy;
using Leistd.Authorization.Permissions;
using Leistd.Auditing.Abstractions;
using Leistd.MultiTenancy.Abstractions;

namespace Leistd.Authorization.EntityFrameworkCore.Entities;

/// <summary>
/// 授予主体的授权版本，每次写入递增。
/// </summary>
/// <remarks>
/// 一张表同时支撑两件事：批量替换的乐观并发（返回 409 而非静默覆盖），以及客户端有效权限
/// 缓存的过期判断。角色成员变更不需要在此扇出写入，
/// 因为主体版本由"用户版本 + 参与拼接的角色版本集合"共同构成，见
/// <see cref="SubjectPermissionGrants.VersionToken"/>。
/// </remarks>
public class AuthorizationVersionRecord : IModificationAuditedObject, IMultiTenant
{
    /// <summary>版本记录 ID（有序 Guid v7）。</summary>
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>所属租户 Id，null 为宿主。版本随授予按租户分区，各租户独立演进。</summary>
    public Guid? TenantId { get; set; }

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
