#if (!LocalIdentity)
using CompanyName.ProjectName.Domain.Users.DomainServices;
using Leistd.Security.Users;
using Leistd.UnitOfWork;

namespace CompanyName.ProjectName.Api.Middlewares;

/// <summary>
/// 资源服务形态：首次持令牌访问时，把签发方的主体投影成本地用户行。
/// </summary>
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
/// 失败时记 Warning 并放行，由授权按"没有成员行 = 没有权限"自然处理。</para>
/// <para><b>代价与边界。</b>每个已认证请求多一次主键查询（命中即返回）。
/// 首次访问的并发由重试一次兜底，见下面的注释。</para>
/// <para><b>做不到的事要如实说：无法按人名预先授权。</b>那需要一条向签发方查人的契约，
/// 本模板没有——用手填 GUID 假装有，才是前面那个缺陷的由来。</para>
/// <para><b>如果确实拿到了对方的 <c>sub</c>，两种授权的可行性并不一样，别一概而论：</b></para>
/// <list type="bullet">
/// <item><description><b>权限授予可以先落。</b><c>PermissionGrantRecord</c> 只存
/// <c>ProviderName</c> + <c>ProviderKey</c> 两个字符串，对 <c>Users</c> 没有外键，
/// 因此写一条 <c>("U", sub)</c> 的授予不需要用户行先存在；等这个人第一次带令牌来，
/// 投影建行，授予立刻生效。</description></item>
/// <item><description><b>角色授予不行。</b><c>UserRole.UserId</c> 对 <c>Users</c> 有外键，
/// 用户行不存在时插入直接违反外键。要给角色，只能等投影之后。</description></item>
/// </list>
/// </remarks>
public sealed class ResourceUserProvisioningMiddleware(
    RequestDelegate next,
    ILogger<ResourceUserProvisioningMiddleware> logger)
{
    public async Task InvokeAsync(
        HttpContext context,
        ICurrentUser currentUser,
        IUnitOfWorkManager unitOfWorkManager,
        UserDomainService userDomainService)
    {
        if (currentUser.IsAuthenticated && currentUser.Id is { } subjectId)
        {
            try
            {
                await ProjectAsync(context, unitOfWorkManager, userDomainService, currentUser, subjectId);
            }
            catch (Exception first) when (first is not OperationCanceledException)
            {
                // 首次访问的并发：同一个 sub 的两个请求同时插入，输的那个在提交时撞主键。
                // 这在资源服务里不是罕见路径——前端登录后往往并行发好几个请求，第一次访问正好都在投影。
                // 重来一次就会读到赢家写下的行，用户这次请求照样有权限。只重试一次，不做退避循环：
                // 第二次还失败就不是竞争，是真有问题。
                try
                {
                    await ProjectAsync(context, unitOfWorkManager, userDomainService, currentUser, subjectId);
                }
                catch (Exception second) when (second is not OperationCanceledException)
                {
                    logger.LogWarning(
                        second,
                        "Projecting issuer subject {SubjectId} failed; continuing without a local user row.",
                        subjectId);
                }
            }
        }

        await next(context);
    }

    private static async Task ProjectAsync(
        HttpContext context,
        IUnitOfWorkManager unitOfWorkManager,
        UserDomainService userDomainService,
        ICurrentUser currentUser,
        Guid subjectId)
    {
        // 独立工作单元：投影是请求的前置动作，不该被后续业务失败连带回滚——
        // 回滚了下一次请求还要再建一次，而这一行的存在与业务是否成功无关。
        using var unitOfWork = await unitOfWorkManager.BeginAsync(requiresNew: true);
        await userDomainService.EnsureProjectedAsync(
            subjectId,
            currentUser.Username,
            currentUser.Email,
            currentUser.Name,
            context.RequestAborted);
        await unitOfWork.CompleteAsync(context.RequestAborted);
    }
}
#endif
