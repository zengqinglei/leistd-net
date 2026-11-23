using Leistd.MultiTenancy.Tenancy;

namespace Leistd.Settings.EntityFrameworkCore.Entities;

/// <summary>某一租户（宿主为宿主）下一项设置测试最近一次的结果，再测即覆盖。</summary>
/// <remarks>只存结论、失败码、时间与测试人快照，不存设置值与对方的响应。</remarks>
public class SettingCheckRecord : IMultiTenant
{
    /// <summary>主键（有序 Guid v7）。</summary>
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <inheritdoc />
    public Guid? TenantId { get; set; }

    /// <summary>租户键，与 <c>Name</c> 组成唯一索引；写法与 <see cref="SettingRecord.ScopeKey"/> 的租户级相同。</summary>
    public string ScopeKey { get; set; } = default!;

    /// <summary>主设置名。</summary>
    public string Name { get; set; } = default!;

    /// <summary>是否通过。</summary>
    public bool Passed { get; set; }

    /// <summary>未通过时的失败码。</summary>
    public string? FailureCode { get; set; }

    /// <summary>测试时间（UTC）；首次测试尚未完成时只有运行登记，没有结果，为空。</summary>
    public DateTime? CheckTime { get; set; }

    /// <summary>测试人标识。</summary>
    public string? CheckerId { get; set; }

    /// <summary>测试人显示名快照。</summary>
    public string? CheckerName { get; set; }

    /// <summary>测试之后主设置或依赖设置被写入或清除过。</summary>
    public bool IsStale { get; set; }

    /// <summary>正在进行的运行登记；写入或清除主设置、依赖设置时作废，保存结果后清除。</summary>
    public Guid? RunId { get; set; }
}
