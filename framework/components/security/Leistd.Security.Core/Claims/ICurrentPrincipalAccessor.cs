using System.Security.Claims;

namespace Leistd.Security.Claims;

/// <summary>
/// 提供当前认证主体并支持临时切换。
/// </summary>
public interface ICurrentPrincipalAccessor
{
    /// <summary>
    /// 获取当前认证主体。
    /// </summary>
    ClaimsPrincipal? Principal { get; }

    /// <summary>
    /// 临时切换认证主体，并在释放返回句柄时恢复父主体。
    /// </summary>
    /// <param name="principal">要切换到的认证主体</param>
    /// <returns>Dispose 时自动恢复到之前的认证主体</returns>
    /// <remarks>
    /// <para>使用场景：单元测试中模拟用户、请求内的跨用户操作。</para>
    /// <para><b>非 HTTP 入口（后台任务、消息消费者、Hub 调用）不要只用它</b>：
    /// 它只切换主体，租户与链路标识仍是进入前的状态——结果是"有主体但租户是宿主视角"，
    /// 查询只见宿主行、写入落错归属，且没有信号。那些入口用
    /// <c>Leistd.AmbientContext.IAmbientContext.Begin(principal)</c>，
    /// 它会把已注册的各维度一并建立。</para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // 后台任务里临时以指定主体运行，作用域退出即还原
    /// using (principalAccessor.Change(systemPrincipal))
    /// {
    ///     await importer.RunAsync(ct);   // 期间的审计与权限判定都按该主体
    /// }
    /// </code>
    /// </example>
    IDisposable Change(ClaimsPrincipal principal);
}
