using Microsoft.Extensions.Configuration;

namespace CompanyName.ProjectName.Infrastructure.TenantConnections;

internal sealed class ConfigurationSecretResolver(IConfiguration configuration) : ISecretResolver
{
    public Task<string> ResolveAsync(string secretReference, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var value = configuration[$"TenantSecrets:{secretReference}"];
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException("The tenant database Secret could not be resolved.");
        }

        return Task.FromResult(value);
    }
}
