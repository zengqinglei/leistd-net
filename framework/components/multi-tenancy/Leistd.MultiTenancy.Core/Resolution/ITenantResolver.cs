namespace Leistd.MultiTenancy.Resolution;

/// <summary>
/// 按注册顺序解析当前请求的租户。
/// </summary>
public interface ITenantResolver
{
    /// <summary>
    /// 执行租户解析链。
    /// </summary>
    Task<TenantResolveResult> ResolveAsync();
}

/// <summary>
/// 表示租户解析结果。
/// </summary>
public class TenantResolveResult
{
    /// <summary>
    /// 获取或设置解析出的租户标识或名称；<see langword="null"/> 表示宿主。
    /// </summary>
    public string? TenantIdOrName { get; set; }

    /// <summary>
    /// 获取或设置终止解析链的贡献者名称；<see langword="null"/> 表示没有贡献者给出结论。
    /// </summary>
    /// <remarks>
    /// 只记命中的那一个，不记全部执行过的：这是诊断信息，而解析在每个请求上都会发生，
    /// 为一行 Debug 日志在热路径上分配一个列表并不值得。
    /// </remarks>
    public string? AppliedResolver { get; set; }
}
