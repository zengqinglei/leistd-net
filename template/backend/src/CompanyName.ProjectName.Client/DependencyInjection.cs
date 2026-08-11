using Leistd.ServiceClient;
using Leistd.ServiceClient.OAuth;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CompanyName.ProjectName.Client;

/// <summary>
/// 客户端注册入口（在调用方服务的 Program.cs 使用）。
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// 注册本服务客户端：装配标准调用管道（日志、TraceId 透传、用户上下文头），
    /// 配置节 <c>Leistd:ServiceClients:MyProject</c>；存在 <c>Auth</c> 子节时自动启用
    /// OAuth2 client credentials 认证。
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="configuration">应用配置</param>
    /// <returns><see cref="IHttpClientBuilder"/>，可继续叠加弹性等处理器</returns>
    public static IHttpClientBuilder AddMyProjectClient(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var builder = services
            .AddServiceClient<IMyProjectClient, MyProjectClient, MyProjectClientOptions>(
                MyProjectClientDefaults.ServiceName, configuration);

        var authSection = configuration.GetSection(
            $"{Leistd.ServiceClient.DependencyInjection.ConfigurationSectionPrefix}:{MyProjectClientDefaults.ServiceName}:Auth");
        if (authSection.Exists())
        {
            builder.AddClientCredentials(configuration);
        }

        return builder;
    }
}
