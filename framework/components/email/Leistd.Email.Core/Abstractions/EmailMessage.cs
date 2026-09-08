namespace Leistd.Email.Abstractions;

/// <summary>
/// 一封待发送的邮件。
/// </summary>
/// <example>
/// <code>
/// await emailSender.SendAsync(new EmailMessage
/// {
///     To = "user@example.com",
///     Subject = "Account Registration Verification Code",
///     Body = $"&lt;p&gt;{code}&lt;/p&gt;",
/// }, ct);
/// </code>
/// </example>
public sealed class EmailMessage
{
    /// <summary>获取收件人地址。</summary>
    public required string To { get; init; }

    /// <summary>获取邮件主题。</summary>
    public required string Subject { get; init; }

    /// <summary>获取邮件正文。</summary>
    public required string Body { get; init; }

    /// <summary>获取正文是否为 HTML；默认 <see langword="true"/>。</summary>
    public bool IsBodyHtml { get; init; } = true;

    /// <summary>
    /// 获取发件地址；为空时使用 Provider 配置的默认发件身份。
    /// </summary>
    /// <remarks>
    /// 显式给出地址时，使用 <see cref="FromName"/>；显示名为空则不署名，不回退到默认显示名。
    /// </remarks>
    public string? FromAddress { get; init; }

    /// <summary>获取发件显示名；语义见 <see cref="FromAddress"/>。</summary>
    public string? FromName { get; init; }
}
