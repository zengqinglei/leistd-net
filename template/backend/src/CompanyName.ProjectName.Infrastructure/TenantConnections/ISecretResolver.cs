namespace CompanyName.ProjectName.Infrastructure.TenantConnections;

/// <summary>
/// 以 Secret Reference 解析真实值。默认实现读取部署注入的配置，生产可替换为云 Secret Provider。
/// </summary>
public interface ISecretResolver
{
    Task<string> ResolveAsync(string secretReference, CancellationToken cancellationToken = default);
}
