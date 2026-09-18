#if (LocalIdentity)
namespace CompanyName.ProjectName.Domain.Auth.Options;

/// <summary>
/// 登录会话的时效（由宿主按会话 Cookie 的过期时间配置，两者必须一致）
/// </summary>
public sealed class UserSessionOptions
{
    /// <summary>
    /// 空闲多久视为会话结束。与 Cookie 的滑动过期同值：
    /// 比它短，Cookie 还有效时会话已被判过期；比它长，设备列表里会留着早已失效的会话。
    /// </summary>
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromDays(7);
}
#endif
