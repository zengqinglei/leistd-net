#if (LocalIdentity)
using CompanyName.ProjectName.Domain.Users.Errors;
using CompanyName.ProjectName.Application.Auth.SignIn;
using Leistd.UnitOfWork.Attributes;
using Leistd.ExceptionHandling;
using CompanyName.ProjectName.Application.Auth.Dtos;
using CompanyName.ProjectName.Domain.Auth.Abstractions;
using CompanyName.ProjectName.Domain.Auth.DomainServices;
using CompanyName.ProjectName.Domain.Users.DomainServices;
using CompanyName.ProjectName.Domain.Users.Repositories;
using Leistd.Ddd.Application.AppServices;
using CompanyName.ProjectName.Application.OperationRecords.Provider;
using CompanyName.ProjectName.Domain.Auth.Entities;
using CompanyName.ProjectName.Domain.Users.Entities;
using Leistd.Ddd.Domain.Repositories;
using Leistd.ObjectMapping.Abstractions;
using Leistd.OperationRecords.Models;
using Leistd.OperationRecords.Recording;
using Leistd.Security.Users;

namespace CompanyName.ProjectName.Application.Auth.AppServices;

/// <summary>
/// 外部身份验证服务
/// </summary>
internal sealed class ExternalAuthAppService(
    ExternalAuthDomainService externalAuthDomainService,
    UserDomainService userDomainService,
    IRoleRepository roleRepository,
    SessionSignInService sessionSignInService,
    IRepository<User, Guid> userRepository,
    IRepository<ExternalLoginConnection, Guid> externalLoginConnectionRepository,
    ICurrentUser currentUser,
    IOperationRecorder operationRecorder,
    IObjectMapper objectMapper) : BaseAppService(), IExternalAuthAppService
{
    /// <summary>
    /// 处理外部登录回调
    /// </summary>
    /// <remarks>
    /// 建用户、分配默认角色、建外部登录连接三次写入必须同生共死：缺了连接行，
    /// 同一外部账号下次登录会再建一个用户。
    /// </remarks>
    [UnitOfWork]
    public async Task<SessionLoginResult> AuthenticateExternalUserAsync(
        string provider,
        ExternalUserInfo externalUserInfo,
        CancellationToken cancellationToken = default)
    {
        var (user, created) = await externalAuthDomainService.FindOrCreateUserAsync(
            provider,
            externalUserInfo,
            cancellationToken);

        // 新建用户带出刚分配的角色名（关联行在本工作单元内尚未落库，回查不到）；
        // 既有用户传 null，由 SessionSignInService 按已落库的角色回查
        List<string>? roleNames = null;
        if (created)
        {
            var defaultRoles = await roleRepository.GetDefaultRolesAsync(cancellationToken);
            await userDomainService.AssignRolesAsync(user.Id, defaultRoles, cancellationToken);
            roleNames = [.. defaultRoles.Select(role => role.Name)];
        }

        return await sessionSignInService.StartAsync(user, roleNames, cancellationToken);
    }

    /// <summary>
    /// 本人的外部账号绑定：部署已配置的每个提供商，附带本人在其下的绑定
    /// </summary>
    public async Task<ExternalLoginsOutputDto> GetCurrentUserExternalLoginsAsync(IEnumerable<string> availableProviders, CancellationToken cancellationToken = default)
    {
        var user = await GetCurrentUserEntityAsync(cancellationToken);
        var links = (await externalLoginConnectionRepository.GetListAsync(c => c.UserId == user.Id, cancellationToken)).ToList();

        return new ExternalLoginsOutputDto
        {
            HasPassword = user.PasswordHash is not null,
            Providers = availableProviders
                .OrderBy(p => p, StringComparer.Ordinal)
                .Select(p => new ExternalLoginProviderOutputDto
                {
                    Provider = p,
                    Link = links
                        .Where(l => string.Equals(l.Provider, p, StringComparison.OrdinalIgnoreCase))
                        .Select(l => objectMapper.Map<ExternalLoginConnection, ExternalLoginLinkOutputDto>(l))
                        .FirstOrDefault()
                })
                .ToList()
        };
    }

    /// <summary>
    /// 把外部身份绑定到当前用户（外部授权回来后调用）
    /// </summary>
    [UnitOfWork]
    public async Task LinkCurrentUserAsync(
        string provider,
        ExternalUserInfo externalUserInfo,
        CancellationToken cancellationToken = default)
    {

        var user = await GetCurrentUserEntityAsync(cancellationToken);
        var link = await externalAuthDomainService.LinkAsync(user, provider, externalUserInfo, cancellationToken);

        await operationRecorder.RecordSucceededAsync(
            OperationRecordActions.AuthExternalLoginLinked,
            // 目标名用提供商侧标签：审计要回答"绑定的是哪个外部账号"，本地用户名回答不了这个。
            // 审计按设计显示可公开展示的值（见 operation-records 的 LocalizationData 约定），
            // 与日志口径不同——日志里联系方式要脱敏，审计是既成事实的记录
            OperationTarget.For(link.Id, $"{provider}: {externalUserInfo.ProviderAccountLabel}"),
            OperationRecordAuthorizations.AuthenticatedSelf,
            cancellationToken);
    }

    /// <summary>
    /// 解绑当前用户的一个外部账号
    /// </summary>
    /// <remarks>不存在或不属于本人时静默成功：解绑是幂等的，也不借此透露别人的绑定 Id 是否存在。</remarks>
    // 删除绑定、轮换安全版本与成功记录同生共死：只删了绑定而版本没轮换，此前的登录挑战仍能完成
    [UnitOfWork]
    public async Task UnlinkCurrentUserAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var user = await GetCurrentUserEntityAsync(cancellationToken);
        var removed = await externalAuthDomainService.UnlinkAsync(user, id, cancellationToken);
        if (removed is null)
            return;

        await operationRecorder.RecordSucceededAsync(
            OperationRecordActions.AuthExternalLoginUnlinked,
            OperationTarget.For(removed.Id, $"{removed.Provider}: {removed.ProviderAccountLabel}"),
            OperationRecordAuthorizations.AuthenticatedSelf,
            cancellationToken);
    }

    private async Task<User> GetCurrentUserEntityAsync(CancellationToken cancellationToken)
    {
        var userId = currentUser.Id!.Value;
        return await userRepository.GetByIdAsync(userId, cancellationToken)
            ?? throw new BusinessException(UserErrorCodes.NotFound, $"User {userId} not found.")
                .WithData("Id", userId);
    }

}
#endif
