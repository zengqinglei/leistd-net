#if (RemoteTokenAuth)
namespace CompanyName.ProjectName.Domain.Systems.Abstractions;

/// <summary>从签发方读取已登记的系统入口。</summary>
public interface IIdentitySystemDirectory
{
    Task<IReadOnlyList<SystemDirectoryEntry>> GetListAsync(CancellationToken cancellationToken = default);
}
#endif
