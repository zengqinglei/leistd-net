#if (!LocalIdentity)
using CompanyName.ProjectName.Api.Auth.Authorization;
using CompanyName.ProjectName.Application.Users.AppServices;
using CompanyName.ProjectName.Domain.Users.ValueObjects;
using Leistd.MultiTenancy.Context;
using Leistd.Security.Users;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CompanyName.ProjectName.Api.Middlewares;

/// <summary>资源服务形态：首次持令牌访问时，把签发方的主体投影成本地用户行。</summary>
/// <remarks>
/// <para><b>为什么必须是自动的。</b>本形态下本地用户行的主键<b>就是</b>签发方的 <c>sub</c>
/// （见 <c>User</c> 的构造函数），而角色授予按这个主键落。让人手填主体标识，抄错一位得到的是
/// 一条永远匹配不上任何令牌、又不会报错的授权——把机制的缺失转嫁给了使用者。</para>
/// <para><b>放在这个位置。</b>要在 <c>UseAuthentication()</c> 之后（没有主体就没什么可投影的），
/// 也要在 <c>UseMultiTenancy()</c> 之后——用户行是 <c>IMultiTenant</c>，租户没解析出来就会落成宿主行。</para>
/// <para><b>只投影，不回写。</b>用户名、邮箱、显示名归签发方所有，这里每次访问按令牌刷新；
/// 本服务不提供编辑它们的入口，改了也没有回写通道，只会与签发方漂移。本服务自己拥有的是
/// 角色授予与本地启停。</para>
/// <para><b>投影失败不拦请求。</b>JIT 投影的作用是让人能被看见、能被授权，<b>它本身不是安全闸门</b>：
/// 拦住请求并不会让谁更安全，只会让一个可预见的写入故障（签发方改名后与本地另一行撞
/// <c>(TenantId, Username)</c> 唯一索引是最典型的）把该用户的<b>每一个</b>请求都变成 500。
/// 失败时记 Warning 并放行，匿名端点照常可用；需要身份的端点由授权按"没有成员行 = 不可用"拒绝
/// （<see cref="LocalMemberAccessRequirement"/>，403 <c>User:LocalMemberMissing</c>）。</para>
/// <para><b>状态快照。</b>投影读到的这一行的启停状态记进 <see cref="LocalMemberAccessSnapshot"/>，
/// 授权阶段直接复用，正常请求不为判定成员状态再查一次库。</para>
/// <para><b>代价与边界。</b>每个已认证请求多一次主键查询（命中即返回）。
/// 首次建成员时，在同一个工作单元按 Identity 的内置管理员资格引导 Admin。</para>
/// </remarks>
public sealed class ResourceUserProvisioningMiddleware(
    RequestDelegate next,
    ILogger<ResourceUserProvisioningMiddleware> logger)
{
    public async Task InvokeAsync(
        HttpContext context,
        ICurrentUser currentUser,
        ICurrentTenant currentTenant,
        IUserAppService userAppService,
        LocalMemberAccessSnapshot accessSnapshot)
    {
        try
        {
            var status = await ProjectWithRetryAsync();
            if (status is { } accessStatus && currentUser.Id is { } userId)
            {
                accessSnapshot.Record(currentTenant.Id, userId, accessStatus);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(
                exception,
                "Projecting issuer subject {SubjectId} failed; continuing without a local user row.",
                currentUser.Id);
        }

        await next(context);

        async Task<UserAccessStatus?> ProjectWithRetryAsync()
        {
            try
            {
                return await userAppService.EnsureCurrentUserProjectedAsync(context.RequestAborted);
            }
            catch (DbUpdateException exception) when (exception.InnerException is PostgresException
                { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                // 并发首访撞唯一约束后重开工作单元，此时可以读到另一请求提交的成员。
                logger.LogDebug(exception, "Projecting issuer subject {SubjectId} hit a unique constraint; retrying.", currentUser.Id);
                return await userAppService.EnsureCurrentUserProjectedAsync(context.RequestAborted);
            }
        }
    }
}
#endif
