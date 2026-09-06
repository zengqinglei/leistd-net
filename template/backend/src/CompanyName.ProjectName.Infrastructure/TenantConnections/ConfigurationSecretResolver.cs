using Microsoft.Extensions.Configuration;
using Leistd.ExceptionHandling;

namespace CompanyName.ProjectName.Infrastructure.TenantConnections;

internal sealed class ConfigurationSecretResolver(IConfiguration configuration) : ISecretResolver
{
    public Task<string> ResolveAsync(string secretReference, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var value = configuration[$"TenantSecrets:{secretReference}"];
        if (string.IsNullOrWhiteSpace(value))
        {
            // Secret 取不到通常是部署侧没注入或 Secret 后端暂时不可达——
            // 503 让调用方知道"稍后可重试"，而 500 会被当成代码故障。
            // 消息里只放引用名，绝不放解析结果
            throw new ServiceUnavailableException(
                $"Tenant database Secret '{secretReference}' could not be resolved.");
        }

        return Task.FromResult(value);
    }
}
