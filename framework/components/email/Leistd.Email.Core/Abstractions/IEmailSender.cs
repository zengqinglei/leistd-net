namespace Leistd.Email.Abstractions;

/// <summary>
/// 发送一封邮件。
/// </summary>
/// <remarks>
/// 投递失败通过异常报告，由调用方决定补偿。
/// 不需要实际投递的环境可显式注册 <see cref="Leistd.Email.NullEmailSender"/>，它不是失败兜底。
/// </remarks>
public interface IEmailSender
{
    /// <summary>发送邮件；失败抛异常。</summary>
    /// <param name="message">待发送的邮件。</param>
    /// <param name="cancellationToken">取消标记。</param>
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}
