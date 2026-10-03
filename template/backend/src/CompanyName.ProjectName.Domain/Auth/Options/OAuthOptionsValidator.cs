#if (LocalIdentity)
using Microsoft.Extensions.Options;

namespace CompanyName.ProjectName.Domain.Auth.Options;

/// <summary>
/// 令牌证书集合的结构校验：不用开发证书时，签名与加密证书各至少一项、每项给出路径。
/// </summary>
/// <remarks>
/// 宿主组合 OpenIddict 时就要读这些证书（早于启动期校验），组合期复用同一个验证器，不另写一套条件。
/// 文件能否加载、有没有 RSA 私钥、是否重复由宿主加载证书时判断。
/// </remarks>
public sealed class OAuthOptionsValidator : IValidateOptions<OAuthOptions>
{
    public ValidateOptionsResult Validate(string? name, OAuthOptions options)
    {
        if (options.UseDevelopmentCertificates)
            return ValidateOptionsResult.Success;

        var failures = new List<string>();
        ValidateCertificates(options.SigningCertificates, $"{OAuthOptions.SectionName}:SigningCertificates", failures);
        ValidateCertificates(options.EncryptionCertificates, $"{OAuthOptions.SectionName}:EncryptionCertificates", failures);
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidateCertificates(OAuthCertificate[] certificates, string key, List<string> failures)
    {
        if (certificates.Length == 0)
        {
            failures.Add($"{key} requires at least one entry (an RSA certificate distinct from the HTTPS certificate, " +
                $"e.g. {key}:0:Path) unless {OAuthOptions.SectionName}:UseDevelopmentCertificates is enabled for local development.");
            return;
        }
        for (var index = 0; index < certificates.Length; index++)
        {
            if (string.IsNullOrWhiteSpace(certificates[index].Path))
                failures.Add($"{key}:{index}:Path is required.");
        }
    }
}
#endif
