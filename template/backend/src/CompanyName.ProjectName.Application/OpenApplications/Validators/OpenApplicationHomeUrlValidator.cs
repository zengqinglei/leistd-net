#if (OpenIddictServer)
using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Hosting;

namespace CompanyName.ProjectName.Application.OpenApplications.Validators;

/// <summary>首页地址与系统切换的跨字段校验。</summary>
public sealed class OpenApplicationHomeUrlValidator(IHostEnvironment environment)
{
    public void Validate(string? homeUrl, bool showInSwitcher, string applicationType)
    {
        if (showInSwitcher && applicationType != "web")
            throw new ValidationException(new ValidationResult("Only browser applications can appear in the system switcher.", ["ShowInSwitcher"]), null, showInSwitcher);
        if (!showInSwitcher && string.IsNullOrEmpty(homeUrl)) return;
        if (string.IsNullOrEmpty(homeUrl) || homeUrl.Any(char.IsWhiteSpace) ||
            !Uri.TryCreate(homeUrl, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host) ||
            (uri.Scheme != Uri.UriSchemeHttps && !(environment.IsDevelopment() && uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)))
            throw new ValidationException(new ValidationResult("Home URL must be an absolute HTTPS URL. Development also allows HTTP loopback URLs.", ["HomeUrl"]), null, homeUrl);
    }
}
#endif
