namespace Leistd.Email.Abstractions;

/// <summary>
/// 一封待发送的邮件。
/// </summary>
/// <remarks>
/// 用对象而非多个 <c>SendAsync</c> 重载承载参数：日后追加 <c>Cc</c>、附件等维度只是新增
/// <see langword="init"/> 属性，对既有调用方非破坏；重载会随维度组合膨胀。
/// </remarks>
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
    /// <remarks>
    /// 取值错误不会报错，只会让收件人看到裸 HTML 源码或一封没有排版的信。
    /// </remarks>
    public bool IsBodyHtml { get; init; } = true;

    /// <summary>
    /// 获取发件地址；<see langword="null"/> 时使用 Provider 配置的默认发件地址。
    /// </summary>
    /// <remarks>
    /// 发件地址与显示名<b>作为一对</b>处理：本属性非 <see langword="null"/> 时
    /// <see cref="FromName"/> 按原样使用（为 <see langword="null"/> 即没有显示名），
    /// 不会与配置里的默认显示名拼在一起，避免出现"自定义地址 + 默认署名"这种没人想要的组合。
    /// </remarks>
    public string? FromAddress { get; init; }

    /// <summary>获取发件显示名；语义见 <see cref="FromAddress"/>。</summary>
    public string? FromName { get; init; }
}
