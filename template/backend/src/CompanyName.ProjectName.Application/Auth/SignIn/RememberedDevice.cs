#if (LocalIdentity)
namespace CompanyName.ProjectName.Application.Auth.SignIn;

/// <summary>此浏览器完成独立第二步时的用户、租户、安全版本与签发时刻。</summary>
public sealed record RememberedDevice(Guid UserId, Guid? TenantId, string SecurityStamp, DateTime IssuedAt);
#endif
