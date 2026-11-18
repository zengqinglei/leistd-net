#if (RemoteTokenAuth)
using System.Net;
using CompanyName.ProjectName.Domain.Users.Abstractions;
using Leistd.ServiceClient.Http;
using Microsoft.Extensions.Logging;

namespace CompanyName.ProjectName.Infrastructure.Users;

/// <inheritdoc cref="IIdentityAdministrator"/>
internal sealed class IdentityAdministrator(
    IIdentityAdministratorsApi api,
    ILogger<IdentityAdministrator> logger) : IIdentityAdministrator
{
    public async Task<bool> IsAdministratorAsync(Guid? tenantId, Guid userId, CancellationToken cancellationToken = default)
    {
        using var response = await api.GetAsync(tenantId?.ToString() ?? "host", userId, cancellationToken);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            logger.LogError("Identity refused the built-in administrator lookup with HTTP {StatusCode}. Check the administrators.read scope on the Resource machine client.",
                (int)response.StatusCode);
            return false;
        }

        var status = await response.ReadContentAsync<IdentityAdministratorResponse>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Identity returned no administrator status.");
        return status.IsAdministrator;
    }

    private sealed record IdentityAdministratorResponse
    {
        public required bool IsAdministrator { get; init; }
    }
}
#endif
