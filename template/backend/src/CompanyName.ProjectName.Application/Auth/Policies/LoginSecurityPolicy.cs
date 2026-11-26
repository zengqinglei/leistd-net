using CompanyName.ProjectName.Application.Settings.Provider;
using CompanyName.ProjectName.Domain.Users.Policies;
using Leistd.Settings.Resolution;
using CompanyName.ProjectName.Domain.Auth.Options;
using CompanyName.ProjectName.Domain.Auth.Policies;
using Microsoft.Extensions.Options;

namespace CompanyName.ProjectName.Application.Auth.Policies;

/// <summary>当前上下文（租户）生效的登录安全策略。</summary>
/// <param name="Lockout">登录失败锁定策略。</param>
/// <param name="RequireTwoFactor">是否要求所有人启用两步验证。</param>
/// <param name="Sessions">会话时长策略。</param>
/// <param name="RememberDeviceDays">记住设备的天数，0 表示关闭。</param>
public sealed record LoginSecurityPolicy(LoginLockoutPolicy Lockout, bool RequireTwoFactor, UserSessionPolicy Sessions, int RememberDeviceDays);

/// <summary>解析当前租户生效的登录安全策略。</summary>
/// <remarks>与 <see cref="IUserRegistrationPolicyProvider"/> 同型：租户级策略从设置读，而不是 <c>IOptions</c>。</remarks>
public interface ILoginSecurityPolicyProvider
{
    /// <summary>读取当前上下文（租户）生效的登录安全策略。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<LoginSecurityPolicy> GetAsync(CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="ILoginSecurityPolicyProvider" />
/// <param name="settingProvider">按当前上下文解析设置值。</param>
public sealed class LoginSecurityPolicyProvider(
    ISettingProvider settingProvider,
    IOptions<UserSessionOptions> sessionOptions) : ILoginSecurityPolicyProvider
{
    /// <inheritdoc />
    public async Task<LoginSecurityPolicy> GetAsync(CancellationToken cancellationToken = default)
    {
        var options = sessionOptions.Value;
        return new(
            new LoginLockoutPolicy(
                await settingProvider.GetAsync<int>(
                    SettingConstant.Security.LockoutMaxFailedAttempts, cancellationToken),
                TimeSpan.FromMinutes(await settingProvider.GetAsync<int>(
                    SettingConstant.Security.LockoutDurationMinutes, cancellationToken))),
            await settingProvider.GetAsync<bool>(
                SettingConstant.Security.RequireTwoFactor, cancellationToken),
            new UserSessionPolicy(
                await settingProvider.GetAsync<bool>(SettingConstant.Security.AllowKeepSignedIn, cancellationToken),
                TimeSpan.FromHours(options.SessionHours),
                TimeSpan.FromDays(options.KeepSignedInDays),
                TimeSpan.FromDays(Math.Min(options.IdleDays, await settingProvider.GetAsync<int>(
                    SettingConstant.Security.SessionIdleTimeoutDays, cancellationToken)))),
            Math.Min(options.RememberDeviceMaxDays, await settingProvider.GetAsync<int>(
                SettingConstant.Security.RememberDeviceDays, cancellationToken)));
    }
}
