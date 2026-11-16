#if (OpenIddictServer)
using System.Globalization;
using CompanyName.ProjectName.Application.Auth.Options;
using CompanyName.ProjectName.Application.OpenApplications;
using CompanyName.ProjectName.Application.Systems.Dtos;
using Leistd.Ddd.Application.AppServices;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;

namespace CompanyName.ProjectName.Application.Systems.AppServices;

public sealed class SystemAppService(IOpenIddictApplicationManager applications, IOptions<OAuthResourceOptions> options)
    : BaseAppService, ISystemAppService
{
    public async Task<IReadOnlyList<SystemOutputDto>> GetListAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<SystemOutputDto>
        {
            new() { Key = "identity", DisplayName = "Identity", HomeUrl = options.Value.Issuer!, IsCurrent = true }
        };
        await foreach (var application in applications.ListAsync(count: null, offset: null, cancellationToken))
        {
            if (await applications.GetApplicationTypeAsync(application, cancellationToken) != OpenIddictConstants.ApplicationTypes.Web) continue;
            var properties = await applications.GetPropertiesAsync(application, cancellationToken);
            if (!OpenApplicationSettings.ReadShowInSwitcher(properties) || OpenApplicationSettings.ReadHomeUrl(properties) is not { Length: > 0 } homeUrl) continue;
            var key = (await applications.GetClientIdAsync(application, cancellationToken))!;
            result.Add(new()
            {
                Key = key, HomeUrl = homeUrl, IsCurrent = false,
                DisplayName = await applications.GetLocalizedDisplayNameAsync(application, CultureInfo.CurrentUICulture, cancellationToken) ?? key
            });
        }
        return [result[0], .. result.Skip(1).OrderBy(system => system.DisplayName, StringComparer.CurrentCulture).ThenBy(system => system.Key, StringComparer.Ordinal)];
    }
}
#endif
