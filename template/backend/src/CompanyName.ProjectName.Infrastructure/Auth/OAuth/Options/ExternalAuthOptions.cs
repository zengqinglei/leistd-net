#if (ExternalLogin)
namespace CompanyName.ProjectName.Infrastructure.Auth.OAuth.Options;

/// <summary>
/// 外部身份提供商的部署配置。
/// </summary>
internal sealed class ExternalAuthOptions
{
    public const string SectionName = "ExternalAuth";

    public ProviderOptions Github { get; } = new();

    public ProviderOptions Google { get; } = new();

    internal sealed class ProviderOptions
    {
        public string? ClientId { get; set; }

        public string? ClientSecret { get; set; }

        public string? RedirectUri { get; set; }

        public bool IsAvailable =>
            !string.IsNullOrWhiteSpace(ClientId)
            && !string.IsNullOrWhiteSpace(ClientSecret)
            && !string.IsNullOrWhiteSpace(RedirectUri);
    }
}
#endif
