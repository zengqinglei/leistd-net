using CompanyName.ProjectName.Client.Dtos;
using Leistd.ServiceClient.Http;

namespace CompanyName.ProjectName.Client;

/// <summary>
/// <see cref="IMyProjectClient"/> 默认实现。
/// 本服务的 API 直接返回 DTO（错误为 ProblemDetails），因此用
/// <c>ReadContentAsync</c> 反序列化并还原远端错误为 <c>RemoteServiceException</c>。
/// </summary>
/// <param name="httpClient">由 AddServiceClient 装配标准管道的 HttpClient</param>
public class MyProjectClient(HttpClient httpClient) : IMyProjectClient
{
    /// <inheritdoc />
    public async Task<ServiceInfoDto?> GetServiceInfoAsync(CancellationToken cancellationToken = default)
    {
        var response = await httpClient.GetAsync("api/v1/service-info", cancellationToken);
        return await response.ReadContentAsync<ServiceInfoDto>(cancellationToken: cancellationToken);
    }

#if (IncludeIdentity)
    /// <inheritdoc />
    public async Task<WhoAmIDto?> WhoAmIAsync(CancellationToken cancellationToken = default)
    {
        var response = await httpClient.GetAsync("api/v1/service-info/whoami", cancellationToken);
        return await response.ReadContentAsync<WhoAmIDto>(cancellationToken: cancellationToken);
    }
#endif
}
