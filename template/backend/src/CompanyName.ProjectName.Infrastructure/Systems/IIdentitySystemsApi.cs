#if (RemoteTokenAuth)
using Refit;

namespace CompanyName.ProjectName.Infrastructure.Systems;

/// <summary>以机器身份读取 Identity 的系统目录。</summary>
public interface IIdentitySystemsApi
{
    [Get("/api/v1/systems")]
    Task<HttpResponseMessage> GetListAsync([Header("Accept-Language")] string culture, CancellationToken cancellationToken = default);
}
#endif
