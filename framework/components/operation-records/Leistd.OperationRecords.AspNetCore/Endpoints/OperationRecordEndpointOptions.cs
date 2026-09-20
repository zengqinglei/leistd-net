namespace Leistd.OperationRecords.AspNetCore.Endpoints;

/// <summary>
/// 操作记录端点的授权口径，全部必填。
/// </summary>
/// <remarks>
/// 组件不内置默认策略：审计内容本身是敏感数据，默认放行就是泄露，而默认策略名又替宿主决定了权限词汇。
/// 漏配任何一项，<c>MapOperationRecords</c> 在映射时就抛出。
/// </remarks>
public sealed class OperationRecordEndpointOptions
{
    /// <summary>查看记录与筛选项所需的授权策略名。</summary>
    public string ReadPolicy { get; set; } = string.Empty;

    /// <summary>导出所需的授权策略名，也作为导出记录的授权依据。</summary>
    public string ExportPolicy { get; set; } = string.Empty;

    /// <summary>导出成功后记下的动作码，须已登记（如 <c>operation-records.exported</c>）。</summary>
    public string ExportAction { get; set; } = string.Empty;

    // 缺必填项抛 ArgumentException，ParamName 即属性名
    internal void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ReadPolicy);
        ArgumentException.ThrowIfNullOrWhiteSpace(ExportPolicy);
        ArgumentException.ThrowIfNullOrWhiteSpace(ExportAction);
    }
}
