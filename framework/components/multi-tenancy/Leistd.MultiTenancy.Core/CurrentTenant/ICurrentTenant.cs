namespace Leistd.MultiTenancy;

/// <summary>
/// 当前租户环境上下文
/// </summary>
public interface ICurrentTenant
{
    /// <summary>
    /// 是否存在租户上下文（<c>false</c> 即宿主视角）
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// 当前租户 Id，<c>null</c> 表示宿主
    /// </summary>
    Guid? Id { get; }

    /// <summary>
    /// 当前租户名称（可能为 null，仅用于展示与日志）
    /// </summary>
    string? Name { get; }

    /// <summary>
    /// 切换当前租户，释放返回句柄时恢复父上下文（支持嵌套）
    /// </summary>
    /// <param name="id">目标租户 Id，传 <c>null</c> 切换到宿主视角</param>
    /// <param name="name">目标租户名称（可选）</param>
    IDisposable Change(Guid? id, string? name = null);
}
