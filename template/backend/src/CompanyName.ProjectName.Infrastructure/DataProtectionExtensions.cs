using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace CompanyName.ProjectName.Infrastructure;

/// <summary>
/// 提供本服务的 Data Protection 密钥环注册。
/// </summary>
public static class DataProtectionExtensions
{
    /// <summary>
    /// 注册持久化的 Data Protection 密钥环：配置了 Redis 时存 Redis，否则存文件系统。
    /// </summary>
    /// <remarks>
    /// <para>除认证 Cookie 与防伪令牌外，控制库里租户独立库的连接串也用这套密钥环加密。
    /// 读写同一控制库的进程（API、DbMigrator）必须共享同一密钥环与应用名，否则一方写入的
    /// 连接串另一方解不开，该租户的请求与迁移都会被拒绝。</para>
    /// <para>密钥丢失等于连接串丢失：生产环境必须把密钥持久化到共享且有备份的位置。
    /// 不用 Redis 时，用 <c>DataProtection:KeysPath</c> 把各进程指向同一个目录。</para>
    /// </remarks>
    /// <param name="services">服务集合</param>
    /// <param name="configuration">配置</param>
    /// <param name="contentRootPath">未配置 <c>DataProtection:KeysPath</c> 时，文件系统密钥目录的父目录</param>
    public static IServiceCollection AddMyProjectDataProtection(
        this IServiceCollection services,
        IConfiguration configuration,
        string contentRootPath)
    {
        // 连接串使用 StackExchange.Redis 原生格式（host:port,password=...,ssl=true）
        var redisConnStr = configuration.GetConnectionString("Redis");
        if (!string.IsNullOrEmpty(redisConnStr))
        {
            services.AddDataProtection()
                .SetApplicationName("MyProject")
                .PersistKeysToStackExchangeRedis(
                    ConnectionMultiplexer.Connect(redisConnStr),
                    "DataProtection-Keys");
        }
        else
        {
            var configuredPath = configuration["DataProtection:KeysPath"];
            var keysPath = string.IsNullOrWhiteSpace(configuredPath)
                ? Path.Combine(contentRootPath, "DataProtection-Keys")
                : configuredPath;
            Directory.CreateDirectory(keysPath);
            services.AddDataProtection()
                .SetApplicationName("MyProject")
                .PersistKeysToFileSystem(new DirectoryInfo(keysPath));
        }

        return services;
    }
}
