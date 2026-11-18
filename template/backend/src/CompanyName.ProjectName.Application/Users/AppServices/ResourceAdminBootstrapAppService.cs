#if (RemoteTokenAuth)
using CompanyName.ProjectName.Application.Initialization;
using CompanyName.ProjectName.Application.OperationRecords.Provider;
using CompanyName.ProjectName.Domain.Users.DomainServices;
using CompanyName.ProjectName.Domain.Users.Entities;
using CompanyName.ProjectName.Domain.Users.Repositories;
using Leistd.Ddd.Application.AppServices;
using Leistd.OperationRecords.Models;
using Leistd.OperationRecords.Recording;

namespace CompanyName.ProjectName.Application.Users.AppServices;

/// <summary>资源服务首位管理员授予，由部署引导与首次成员投影共用。</summary>
/// <remarks>调用方判定授予资格，并持有初始化锁与同一个工作单元直到提交。</remarks>
public sealed class ResourceAdminBootstrapAppService(
    ISystemInitializer initializer,
    UserDomainService userDomainService,
    IUserRepository userRepository,
    IOperationRecorder recorder) : BaseAppService
{
    /// <summary>部署命令授予 Admin，必要时先建最小成员行。</summary>
    /// <returns>是否新建了成员行。</returns>
    public async Task<bool> GrantAsync(Guid subject, CancellationToken cancellationToken = default)
    {
        var adminRole = await initializer.InitializeWithinLockAsync(cancellationToken);
        var (user, created) = await userDomainService.EnsureSubjectAsync(subject, cancellationToken);
        await AssignAsync(user, adminRole, OperationRecordAuthorizations.DeploymentBootstrap, cancellationToken);
        return created;
    }

    /// <summary>首次建成员时，按 Identity 内置管理员资格授予本地 Admin。</summary>
    public async Task GrantAsync(User user, CancellationToken cancellationToken = default)
    {
        var adminRole = await initializer.InitializeWithinLockAsync(cancellationToken);
        await AssignAsync(user, adminRole, OperationRecordAuthorizations.IdentityAdministrator, cancellationToken);
    }

    private async Task AssignAsync(User user, Role role, string authorization, CancellationToken cancellationToken)
    {
        user.AssignRoles([role.Id]);
        await userRepository.UpdateAsync(user, cancellationToken);
        var key = user.Id.ToString();
        await recorder.RecordSucceededAsync(OperationRecordActions.ResourceAdminGranted,
            OperationTarget.For($"User/{key}", key), authorization, cancellationToken);
    }
}
#endif
