using Leistd.MultiTenancy.Abstractions;

namespace Leistd.Settings.EntityFrameworkCore.Entities;

/// <summary>
/// 表示某一层级上的一个设置值。
/// </summary>
/// <remarks>
/// 层级由 <c>TenantId</c> 与 <c>UserId</c> 共同表达，不引入可独立修改的 Scope 列：
/// <c>TenantId</c> 交给多租户查询过滤器隔离（<see langword="null"/> 即宿主），
/// <c>UserId</c> 为 <see langword="null"/> 即租户级、非空即该用户的用户级。
/// </remarks>
public class SettingRecord : IMultiTenant
{
    /// <summary>主键（有序 Guid v7）。</summary>
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <inheritdoc />
    public Guid? TenantId { get; set; }

    /// <summary>用户标识；<see langword="null"/> 表示租户级。</summary>
    public string? UserId { get; set; }

    /// <summary>
    /// 存储完整性键：把租户与层级编码成非空字符串。
    /// </summary>
    /// <remarks>
    /// 与 <c>Name</c> 组成唯一索引，避免可空 TenantId/UserId 使宿主与租户默认值失去唯一约束。
    /// 由写入端根据当前租户和层级派生，不由调用方单独指定。
    /// </remarks>
    public string ScopeKey { get; set; } = default!;

    /// <summary>设置名称。</summary>
    public string Name { get; set; } = default!;

    /// <summary>设置值。</summary>
    public string Value { get; set; } = default!;
}
