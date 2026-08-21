namespace Leistd.MultiTenancy;

/// <summary>
/// 为当前宿主或租户异步解析最终连接字符串。
/// </summary>
/// <remarks>
/// 实现必须对未知租户、缺失配置和无效 Secret 失败关闭，不得把异常状态回退为共享数据库。
/// </remarks>
public interface ITenantConnectionStringResolver
{
    /// <summary>解析指定名称的最终连接字符串。</summary>
    Task<string> ResolveAsync(
        string connectionStringName,
        CancellationToken cancellationToken = default);
}
