#if (LocalIdentity)
using CompanyName.ProjectName.Application.Auth.Dtos;

namespace CompanyName.ProjectName.Application.Auth.AppServices;

/// <summary>
/// 本人的两步验证：设置、启用、停用与恢复码
/// </summary>
public interface ITwoFactorAppService
{
    /// <summary>当前状态。</summary>
    Task<TwoFactorStatusOutputDto> GetStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 开始设置：生成一个待启用的密钥（几分钟内有效），返回给身份验证器应用添加。
    /// </summary>
    Task<TwoFactorSetupOutputDto> BeginSetupAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 用应用上显示的验证码确认并启用，返回一次性展示的恢复码；本人的其他会话随之退出。
    /// </summary>
    Task<TwoFactorRecoveryCodesOutputDto> EnableAsync(TwoFactorCodeInputDto input, CancellationToken cancellationToken = default);

    /// <summary>停用（要密码与验证码）；本人的其他会话随之退出。所在租户要求两步验证时拒绝。</summary>
    Task DisableAsync(DisableTwoFactorInputDto input, CancellationToken cancellationToken = default);

    /// <summary>重新生成恢复码（要验证码），旧的全部作废。</summary>
    Task<TwoFactorRecoveryCodesOutputDto> RegenerateRecoveryCodesAsync(TwoFactorCodeInputDto input, CancellationToken cancellationToken = default);
}
#endif
