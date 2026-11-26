#if (LocalIdentity)
namespace CompanyName.ProjectName.Domain.Auth.Options;

/// <summary>本地身份的会话与记住设备部署上限。</summary>
public sealed class UserSessionOptions
{
    public const string SectionName = "SessionCookie";

    public int SessionHours { get; set; } = 24;
    public int KeepSignedInDays { get; set; } = 30;
    public int IdleDays { get; set; } = 7;
    public int RememberDeviceMaxDays { get; set; } = 90;

    public TimeSpan IdleTimeout => TimeSpan.FromDays(IdleDays);
}
#endif
