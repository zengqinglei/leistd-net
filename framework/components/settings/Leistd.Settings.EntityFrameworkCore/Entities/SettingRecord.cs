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
    /// 唯一索引落在它与 <c>Name</c> 上，而不是 <c>(TenantId, UserId, Name)</c>：后两者可为
    /// <see langword="null"/>，而 PostgreSQL、SQLite 等把多个 NULL 视为互不相等，宿主级与
    /// 租户级默认值这两个最常用层级根本不受唯一索引约束，并发首次写入会插出重复行，
    /// 之后按名称读取整个层级就会因重复键直接失败。
    /// <para>它不是新的业务事实源：由写入端与 <c>TenantId</c> 同刻从同一个
    /// <c>ICurrentTenant</c> 派生，调用方既不提供也不修改它。</para>
    /// </remarks>
    public string ScopeKey { get; set; } = default!;

    /// <summary>设置名称。</summary>
    public string Name { get; set; } = default!;

    /// <summary>设置值。</summary>
    public string Value { get; set; } = default!;
}
