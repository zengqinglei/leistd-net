#if (RemoteTokenAuth)
using System.Net;
using System.Net.Http.Json;
using CompanyName.ProjectName.Infrastructure.Users;

namespace CompanyName.ProjectName.IntegrationTests.Fixtures;

/// <summary>在 HTTP 客户端边界替换 Identity，保留 Resource 的响应判定与投影逻辑。</summary>
internal sealed class StubIdentityAdministratorsApi(
    Func<string, Guid, CancellationToken, Task<HttpResponseMessage>> respond) : IIdentityAdministratorsApi
{
    public async Task<HttpResponseMessage> GetAsync(string tenant, Guid userId, CancellationToken cancellationToken = default)
        => await respond(tenant, userId, cancellationToken);

    public static HttpResponseMessage Status(bool isAdministrator) => new(HttpStatusCode.OK)
    {
        Content = JsonContent.Create(new { isAdministrator })
    };
}
#endif
