using Leistd.ServiceClient.Refit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CompanyName.ProjectName.Client;

/// <summary>
/// 客户端注册入口（在调用方服务的 Program.cs 使用）。
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// 注册本服务客户端：Refit 接口实现 + 标准调用管道（传输异常、TraceId 透传）
    /// + 统一错误契约（<c>RemoteServiceException</c>），配置节
    /// <c>Leistd:ServiceClients:MyProject</c>。宿主注册全局调用身份，并在返回的
    /// 构建器上明确选择机器认证或 Token Exchange，配置相应的目标范围。
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="configuration">应用配置</param>
    /// <returns><see cref="IHttpClientBuilder"/>，可继续叠加弹性等处理器</returns>
    public static IHttpClientBuilder AddMyProjectClient(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var builder = services
            .AddRefitServiceClient<IMyProjectClient, MyProjectClientOptions>(
                MyProjectClientDefaults.ServiceName, configuration);

        return builder;
    }
}
