namespace CompanyName.ProjectName.Infrastructure.TenantConnections;

/// <summary>
/// 数据库目标配置。仅用于启动期校验，运行期解析仍直接读 <c>IConfiguration</c>。
/// </summary>
/// <remarks>
/// <para>校验的不变量：要么有真实连接串，要么有显式内存库名。缺连接串是部署配置错误，
/// 留到运行期的话每个请求都会失败一次——客户端只看到 500、运维只看到堆栈刷屏，
/// 而进程本身"健康"地跑着。配置错误必须在接流量之前大声失败。</para>
/// <para>两个值都经 <c>Configure&lt;IConfiguration&gt;</c> 从 DI 取，不在组合期直接读：
/// 组合期的配置还不是最终值，集成测试通过 <c>WebApplicationFactory</c> 追加的覆盖此刻尚未合入。
/// 组合期直接读配置的表现是"生产逻辑正确但测试全红"，且报错位置与原因无关。</para>
/// </remarks>
internal sealed class TenantConnectionResolutionOptions
{
    /// <summary>配置键 <c>ConnectionStrings:Default</c></summary>
    public string? DefaultConnectionString { get; set; }

    /// <summary>配置键 <c>Database:InMemoryName</c>。非空表示刻意使用内存库</summary>
    public string? InMemoryName { get; set; }

    /// <summary>是否满足"有一个可用的数据库目标"</summary>
    public bool HasDatabaseTarget =>
        !string.IsNullOrWhiteSpace(DefaultConnectionString) || !string.IsNullOrWhiteSpace(InMemoryName);
}
