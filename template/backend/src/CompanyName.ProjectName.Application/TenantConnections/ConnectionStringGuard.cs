#if (LocalIdentity)
using System.Data.Common;
using Leistd.ExceptionHandling;

namespace CompanyName.ProjectName.Application.TenantConnections;

/// <summary>
/// 连接串的<b>语法</b>守卫：非法即 400，而不是让它一路走到数据库驱动。
/// </summary>
/// <remarks>
/// <para>框架的 <c>ITenantConnectionConfigurationManager.SetAsync</c> 只守编程契约——
/// 它的 <c>ValidateConnectionString</c> 仅检查"非空"与"不超长"，<b>不看语法</b>，
/// 且刻意抛 <see cref="ArgumentException"/>（接口文档把它列为编程错误）。
/// 于是一个手滑填进去的 <c>dsadfdsfsdf</c> 会原样穿过登记、在播种阶段才由
/// <c>NpgsqlConnectionStringBuilder</c> 抛 <see cref="ArgumentException"/>，
/// 以 500「系统异常，请联系管理员」结束——<b>用户填错一个字段，得到的是系统级故障提示</b>。</para>
/// <para>这与连接<b>名</b>是同一类问题，那一侧已经修过（见
/// <c>TenantConnectionAppService.EnsureValidName</c> 的注释：「框架只守编程契约……
/// 而这是调用方自己就能改对的输入错误，应当是 400」）。本类型只是把同一条判据
/// 补到被漏掉的另一侧。</para>
/// <para><b>判据用 <see cref="DbConnectionStringBuilder"/> 而不是 Npgsql 的那个</b>：
/// 前者在 BCL 里，应用层因此不必引用任何数据库驱动（Application 的 csproj 确实没有 Npgsql）；
/// 而且它校验的正是出问题的那一层——<c>键=值;</c> 这个与提供程序无关的语法。
/// 栈里抛异常的也正是它：<c>NpgsqlConnectionStringBuilder</c> 继承自 <c>DbConnectionStringBuilder</c>，
/// 报错来自基类的 <c>set_ConnectionString</c>。</para>
/// <para><b>不在这里判断"库连不连得上"</b>：Host/Database 是否齐全、口令对不对、网络通不通，
/// 都要真的连一次才知道，那属于随后的播种阶段。本守卫只拦"根本不是连接串"这一类，
/// 即无需连库就能判定的错误。</para>
/// <para><b>异常消息不回显连接串</b>，与框架 <c>ValidateConnectionString</c> 同一口径
/// （「异常消息只描述规则，不回显连接串」）。连接串里有数据库口令，回显一次就同时进了
/// 响应体、前端 toast 和服务端日志——三处都撤不回来。</para>
/// </remarks>
internal static class ConnectionStringGuard
{
    /// <summary>
    /// 确认 <paramref name="connectionString"/> 能被解析为分号分隔的键值对；否则抛 400。
    /// </summary>
    /// <remarks>
    /// 空与超长交给框架：调用方传空本就意味着"不分库"，那条路在上游用
    /// <c>string.IsNullOrWhiteSpace</c> 分流，压根不会走到这里。
    /// </remarks>
    public static void EnsureParsable(string connectionString)
    {
        try
        {
            _ = new DbConnectionStringBuilder { ConnectionString = connectionString };
        }
        catch (ArgumentException error)
        {
            throw new BadRequestException(
                "The connection string is malformed. "
                    + "It must be semicolon-separated key=value pairs, "
                    + "for example Host=...;Port=5432;Database=...;Username=...;Password=... "
                    + "The value you entered is not echoed back because it carries database credentials.",
                error)
#if (IncludeLocalization)
                .WithCode("TenantConnection:ConnectionStringInvalid")
#endif
            ;
        }
    }
}
#endif
