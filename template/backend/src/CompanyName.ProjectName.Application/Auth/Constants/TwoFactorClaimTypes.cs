#if (LocalIdentity)
namespace CompanyName.ProjectName.Application.Auth.Constants;

/// <summary>
/// 两步验证相关的会话声明。
/// </summary>
public static class TwoFactorClaimTypes
{
    /// <summary>
    /// 受限会话：租户要求两步验证而本人尚未启用。<b>存在即受限</b>——
    /// 服务端只放行完成设置所需的接口，启用后换发一个不带它的会话。
    /// </summary>
    public const string SetupRequired = "two_factor_setup_required";
}
#endif
