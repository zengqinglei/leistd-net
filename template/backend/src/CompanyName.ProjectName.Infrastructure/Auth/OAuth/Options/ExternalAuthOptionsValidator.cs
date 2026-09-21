#if (ExternalLogin)
using Microsoft.Extensions.Options;

namespace CompanyName.ProjectName.Infrastructure.Auth.OAuth.Options;

internal sealed class ExternalAuthOptionsValidator : IValidateOptions<ExternalAuthOptions>
{
    public ValidateOptionsResult Validate(string? name, ExternalAuthOptions options)
    {
        var failures = new List<string>();
        ValidateProvider("Github", options.Github, failures);
        ValidateProvider("Google", options.Google, failures);

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidateProvider(
        string provider,
        ExternalAuthOptions.ProviderOptions options,
        ICollection<string> failures)
    {
        var prefix = $"{ExternalAuthOptions.SectionName}:{provider}";
        var hasAnyValue = !string.IsNullOrWhiteSpace(options.ClientId)
            || !string.IsNullOrWhiteSpace(options.ClientSecret)
            || !string.IsNullOrWhiteSpace(options.RedirectUri);

        if (!hasAnyValue)
            return;

        if (string.IsNullOrWhiteSpace(options.ClientId))
            failures.Add($"{prefix}:ClientId is required when the provider is configured.");

        if (string.IsNullOrWhiteSpace(options.ClientSecret))
            failures.Add($"{prefix}:ClientSecret is required when the provider is configured.");

        if (string.IsNullOrWhiteSpace(options.RedirectUri))
        {
            failures.Add($"{prefix}:RedirectUri is required when the provider is configured.");
        }
        else if (!Uri.TryCreate(options.RedirectUri, UriKind.Absolute, out var redirectUri)
                 || redirectUri.Scheme is not ("http" or "https"))
        {
            failures.Add($"{prefix}:RedirectUri must be an absolute HTTP or HTTPS URI.");
        }
    }
}
#endif
