#if (RemoteTokenAuth)
namespace CompanyName.ProjectName.Domain.Users.Abstractions;

/// <summary>读取签发方认定的内置管理员资格，供本地首次引导。</summary>
public interface IIdentityAdministrator
{
    Task<bool> IsAdministratorAsync(Guid? tenantId, Guid userId, CancellationToken cancellationToken = default);
}
#endif
