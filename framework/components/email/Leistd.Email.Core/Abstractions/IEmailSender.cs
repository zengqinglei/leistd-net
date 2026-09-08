namespace Leistd.Email.Abstractions;

/// <summary>
/// 发送一封邮件。
/// </summary>
/// <remarks>
/// <b>发送失败一律抛异常</b>，不吞、不降级、不回落到"假装发出去了"。调用方据此决定补偿：
/// 例如注册验证码在发送失败后必须撤回已占用的限流槽位与挑战，静默成功会让用户拿到
/// 一个永远收不到码的挑战。
/// <para>没有可用 SMTP 的环境请由宿主<b>显式</b>注册 <see cref="Leistd.Email.NullEmailSender"/>
/// （<c>AddNullEmailSender()</c>），它是一个自愿选择的空实现，而不是失败时的兜底。</para>
/// </remarks>
public interface IEmailSender
{
    /// <summary>发送邮件；失败抛异常。</summary>
    /// <param name="message">待发送的邮件。</param>
    /// <param name="cancellationToken">取消标记。</param>
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}
