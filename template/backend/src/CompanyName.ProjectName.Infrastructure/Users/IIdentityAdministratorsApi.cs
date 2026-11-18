#if (RemoteTokenAuth)
using Refit;

namespace CompanyName.ProjectName.Infrastructure.Users;

/// <summary>以机器身份向 Identity 读取内置管理员资格。</summary>
public interface IIdentityAdministratorsApi
{
    [Get("/api/v1/internal/administrators/{tenant}/{userId}")]
    Task<HttpResponseMessage> GetAsync(string tenant, Guid userId, CancellationToken cancellationToken = default);
}
#endif
