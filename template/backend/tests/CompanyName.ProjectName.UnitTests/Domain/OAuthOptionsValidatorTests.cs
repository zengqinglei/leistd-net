#if (OpenIddictServer)
using CompanyName.ProjectName.Domain.Auth.Options;

namespace CompanyName.ProjectName.UnitTests.Domain;

/// <summary>
/// 令牌证书集合的结构校验：一条失败一项、以配置键开头，两个集合的问题一次报全。
/// </summary>
public sealed class OAuthOptionsValidatorTests
{
    private readonly OAuthOptionsValidator _validator = new();

    [Fact]
    public void Development_certificates_need_no_certificate_files()
    {
        Assert.True(_validator.Validate(null, new OAuthOptions { UseDevelopmentCertificates = true }).Succeeded);
    }

    [Fact]
    public void Problems_in_both_collections_are_reported_together()
    {
        var options = new OAuthOptions
        {
            SigningCertificates = [new OAuthCertificate { Path = "signing.pfx" }, new OAuthCertificate { Path = "" }],
            EncryptionCertificates = []
        };

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Collection(result.Failures!,
            failure => Assert.StartsWith("OAuth:SigningCertificates:1:Path is required", failure, StringComparison.Ordinal),
            failure => Assert.StartsWith("OAuth:EncryptionCertificates requires at least one entry", failure, StringComparison.Ordinal));
    }
}
#endif
