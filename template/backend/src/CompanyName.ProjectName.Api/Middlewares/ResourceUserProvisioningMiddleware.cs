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
/// <para><b>代价与边界。</b>每个已认证请求多一次主键查询（命中即返回）。首次访问的并发插入由
/// 唯一键兜底：撞了就重读，不把一次正常的竞争变成 500。</para>
/// <para><b>做不到的事要如实说：无法在用户首次登录前预先授权。</b>那需要一条向签发方查人的契约，
/// 本模板没有——用手填 GUID 假装有，才是前面那个缺陷的由来。</para>
/// </remarks>
public sealed class ResourceUserProvisioningMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext context,
        ICurrentUser currentUser,
        IUnitOfWorkManager unitOfWorkManager,
        UserDomainService userDomainService)
    {
        if (currentUser.IsAuthenticated && currentUser.Id is { } subjectId)
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

        await next(context);
    }
}
#endif
