using Leistd.ServiceClient.OAuth;
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
    /// 注册本服务客户端：Refit 接口实现 + 标准调用管道（日志、TraceId 透传、用户上下文头）
    /// + 统一错误契约（<c>RemoteServiceException</c>），配置节
    /// <c>Leistd:ServiceClients:MyProject</c>。调用方配置了全局调用身份
    /// <c>Leistd:ServiceAuth</c> 时自动启用 OAuth2 client credentials 认证
    /// （目标服务 scope 经 <c>Leistd:ServiceClients:MyProject:Scope</c> 指定，可省）。
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

        if (configuration.GetSection(
                Leistd.ServiceClient.OAuth.DependencyInjection.ServiceAuthSectionName).Exists())
        {
            builder.AddClientCredentials(configuration);
        }

        return builder;
    }
}
