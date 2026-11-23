using CompanyName.ProjectName.Application.Settings.Provider;
using Leistd.Email.Smtp;
using Leistd.Email.Smtp.Options;
using Leistd.Settings.Checks;
using Leistd.Settings.Resolution;
using Microsoft.Extensions.Options;

namespace CompanyName.ProjectName.Api.Configuration;

/// <summary>系统设置的"测试"：按当前发信参数连接 SMTP 服务器并认证，不发信。</summary>
/// <remarks>
/// <para>只回答"连得上、认证得过"：发件地址能否被接受、收件人能否收到，要用发信设置下方的"发送测试邮件"真正发一封。</para>
/// <para>先经设置读取器取一次口令：存着的口令解不开时读取抛出，由设置组件记为 <c>secret-unavailable</c>，
/// 而不是用配置里尚未被替换的旧口令测出一个"通过"。</para>
/// </remarks>
internal sealed class SmtpConnectionCheck(
    ISettingProvider settings,
    SmtpEmailSender sender,
    IOptionsMonitor<SmtpOptions> smtp) : ISettingCheck
{
    public async Task<SettingCheckOutcome> ExecuteAsync(SettingCheckContext context, CancellationToken cancellationToken)
    {
        _ = await settings.GetOrNullAsync(SettingConstant.Email.SmtpPassword, cancellationToken);
        if (string.IsNullOrWhiteSpace(smtp.CurrentValue.Host))
        {
            return SettingCheckOutcome.Fail(SettingCheckFailureCodes.NotConfigured);
        }

        return await sender.ProbeAsync(cancellationToken) switch
        {
            SmtpProbeResult.Succeeded => SettingCheckOutcome.Pass(),
            SmtpProbeResult.AuthenticationRejected => SettingCheckOutcome.Fail(SettingCheckFailureCodes.AuthRejected),
            _ => SettingCheckOutcome.Fail(SettingCheckFailureCodes.Unreachable)
        };
    }
}
