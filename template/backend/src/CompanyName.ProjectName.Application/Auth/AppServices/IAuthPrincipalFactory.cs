#if (IncludeIdentity)
using System.Security.Claims;
using CompanyName.ProjectName.Domain.Users.Entities;

namespace CompanyName.ProjectName.Application.Auth.AppServices;

public interface IAuthPrincipalFactory
{
    Task<ClaimsPrincipal> CreateAsync(User user, IEnumerable<string>? scopes = null, CancellationToken cancellationToken = default);
}
#endif
