using Leistd.Auditing;

namespace Leistd.MultiTenancy.EntityFrameworkCore;

/// <summary>
/// 租户持久化实体（宿主侧数据，<b>不实现</b> <see cref="IMultiTenant"/>，不受租户过滤器影响）。
/// 审计字段由审计拦截器统一填充
/// </summary>
/// <remarks>
/// 写入请统一通过 <see cref="ITenantManager"/>：它负责名称归一化与未删除行内的唯一性校验。
/// 直接写库不会留下陈旧读取（存储直接读库），但会绕开这两项校验。
/// </remarks>
public class TenantRecord : IFullAuditedObject
{
    /// <summary>租户 Id（有序 Guid v7）</summary>
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>租户名称（业务标识，大小写不敏感唯一）</summary>
    public string Name { get; set; } = default!;

    /// <summary>归一化名称（查询键）</summary>
    public string NormalizedName { get; set; } = default!;

    /// <summary>显示名称（可选）</summary>
    public string? DisplayName { get; set; }

    /// <summary>是否启用。停用租户的请求会被多租户中间件拒绝</summary>
    public bool IsActive { get; set; } = true;

    /// <inheritdoc />
    public DateTime CreationTime { get; set; }

    /// <inheritdoc />
    public string? CreatorId { get; set; }

    /// <inheritdoc />
    public DateTime? LastModificationTime { get; set; }

    /// <inheritdoc />
    public string? LastModifierId { get; set; }

    /// <inheritdoc />
    public bool IsDeleted { get; set; }

    /// <inheritdoc />
    public DateTime? DeletionTime { get; set; }

    /// <inheritdoc />
    public string? DeleterId { get; set; }
}
