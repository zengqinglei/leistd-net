using System.Globalization;
using CompanyName.ProjectName.Application.Settings.Provider;
using Leistd.Email.Smtp;
using Leistd.Email.Smtp.Options;
using Leistd.Settings.Checks;
using Leistd.Settings.Hosting;

namespace CompanyName.ProjectName.Api.Configuration;

/// <summary>系统设置的"测试"：按这一次读到的发信参数连接 SMTP 服务器并认证，不发信。</summary>
/// <remarks>
/// <para>只回答"连得上、认证得过"：发件地址能否被接受、收件人能否收到，要用发信设置下方的"发送测试邮件"真正发一封。</para>
/// <para>参数取这一次从存储读到的值，没有存值的项取部署配置；存着的口令解不开时读取抛出，由设置组件记为 <c>secret-unavailable</c>。
/// 不读进程里已生效的选项——它叠加了系统设置，另一副本刚改过时可能还是旧值。</para>
/// </remarks>
internal sealed class SmtpConnectionCheck(
    IHostSettingBaseline deployment,
    SmtpEmailSender sender) : ISettingCheck
{
    public async Task<SettingCheckOutcome> ExecuteAsync(SettingCheckContext context, CancellationToken cancellationToken)
    {
        async Task<string?> ValueAsync(string name)
            => await context.GetValueAsync(name, cancellationToken) ?? deployment.GetDeploymentValue(name);

        var host = await ValueAsync(SettingConstant.Email.SmtpHost);
        if (string.IsNullOrWhiteSpace(host))
        {
            return SettingCheckOutcome.Fail(SettingCheckFailureCodes.NotConfigured);
        }

        var defaults = new SmtpOptions();
        var options = new SmtpOptions
        {
            Host = host,
            Port = int.TryParse(await ValueAsync(SettingConstant.Email.SmtpPort), NumberStyles.Integer, CultureInfo.InvariantCulture, out var port)
                ? port
                : defaults.Port,
            EnableSsl = bool.TryParse(await ValueAsync(SettingConstant.Email.SmtpEnableSsl), out var ssl) ? ssl : defaults.EnableSsl,
            Username = await ValueAsync(SettingConstant.Email.SmtpUsername),
            Password = await ValueAsync(SettingConstant.Email.SmtpPassword),
        };

        return await sender.ProbeAsync(options, cancellationToken) switch
        {
            SmtpProbeResult.Succeeded => SettingCheckOutcome.Pass(),
            SmtpProbeResult.AuthenticationRejected => SettingCheckOutcome.Fail(SettingCheckFailureCodes.AuthRejected),
            _ => SettingCheckOutcome.Fail(SettingCheckFailureCodes.Unreachable)
        };
    }
}
