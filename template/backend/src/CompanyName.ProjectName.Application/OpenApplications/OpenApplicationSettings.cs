#if (LocalIdentity)
using System.Text.Json;

namespace CompanyName.ProjectName.Application.OpenApplications;

/// <summary>模板自有的开放应用设置，存于 OpenIddict 应用的 Settings；管理登记与令牌签发共用这一处键名与读法。</summary>
public static class OpenApplicationSettings
{
    /// <summary>系统首页地址的扩展属性键。</summary>
    public const string HomeUri = "home_uri";

    /// <summary>系统切换显示开关的扩展属性键。</summary>
    public const string ShowInSwitcher = "show_in_switcher";

    /// <summary>读取首页地址，未登记时为空。</summary>
    public static string? ReadHomeUrl(IReadOnlyDictionary<string, JsonElement> properties) =>
        properties.TryGetValue(HomeUri, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    /// <summary>读取系统切换开关，未登记时关闭。</summary>
    public static bool ReadShowInSwitcher(IReadOnlyDictionary<string, JsonElement> properties) =>
        properties.TryGetValue(ShowInSwitcher, out var value) && value.ValueKind == JsonValueKind.True;

    /// <summary>会话绑定：此客户端的授权码与刷新令牌依赖签发时的 Identity 会话，会话退出、撤销或空闲到期后不能再换取令牌。</summary>
    /// <remarks>
    /// 这是模板的授权收敛政策，不是 OIDC 后通道登出元数据 <c>backchannel_logout_session_required</c>
    /// （后者只约定 Logout Token 是否带 sid）。浏览器 BFF 类客户端应开启；离线类客户端按需关闭。
    /// </remarks>
    public const string SessionBound = "leistd:session_bound";

    /// <summary>
    /// 读取会话绑定。缺失返回 <c>null</c>（登记早于该设置，需管理员显式选择）；
    /// 无法识别的值按绑定处理，宁可让续期失败也不放宽会话约束。
    /// </summary>
    public static bool? ReadSessionBound(IReadOnlyDictionary<string, string> settings) =>
        settings.TryGetValue(SessionBound, out var value)
            ? !string.Equals(value, "false", StringComparison.Ordinal)
            : null;

    /// <summary>写入会话绑定的规范值。</summary>
    public static string Format(bool sessionBound) => sessionBound ? "true" : "false";
}
#endif
