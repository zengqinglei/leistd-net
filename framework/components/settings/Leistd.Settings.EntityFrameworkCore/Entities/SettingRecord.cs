using Leistd.MultiTenancy.Tenancy;

namespace Leistd.Settings.EntityFrameworkCore.Entities;

/// <summary>某一层级上的一个设置值。</summary>
/// <remarks>
/// 层级由 <c>TenantId</c> 与 <c>UserId</c> 共同表达，不引入可独立修改的 Scope 列：
/// <c>TenantId</c> 交给多租户查询过滤器隔离（<see langword="null"/> 即宿主），
/// <c>UserId</c> 为 <see langword="null"/> 即租户级、非空即该用户的用户级。
/// <para>修改信息由存储在每次新增与改值时按写入入口给的值写入，语义是"最后一次写入"。列名沿用审计约定，
/// 但不实现审计接口：三列由同一来源负责，审计拦截器不会用另一个时钟或主体改写其中两列。</para>
/// </remarks>
public class SettingRecord : IMultiTenant
{
    /// <summary>主键（有序 Guid v7）。</summary>
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <inheritdoc />
    public Guid? TenantId { get; set; }

    /// <summary>用户标识；<see langword="null"/> 表示租户级。</summary>
    public string? UserId { get; set; }

    /// <summary>把租户与层级编码成的非空键，与 <c>Name</c> 组成唯一索引；由存储按当前租户和层级派生。</summary>
    public string ScopeKey { get; set; } = default!;

    /// <summary>设置名称。</summary>
    public string Name { get; set; } = default!;

    /// <summary>设置值。</summary>
    public string Value { get; set; } = default!;

    /// <summary>最后一次写入的时间；修改信息上线之前写入、从未再写过的行为 <see langword="null"/>。</summary>
    public DateTime? LastModificationTime { get; set; }

    /// <summary>最后一次写入人的标识；无主体写入时为 <see langword="null"/>。</summary>
    public string? LastModifierId { get; set; }

    /// <summary>最后一次写入人的显示名快照；无主体写入时为 <see langword="null"/>。</summary>
    public string? LastModifierName { get; set; }
}
