namespace Leistd.Email.Exceptions;

/// <summary>发信在取得接受确认之前失败，且失败能归入 <see cref="EmailDeliveryFailureKind"/> 的某一类。</summary>
/// <remarks>
/// 三类失败都发生在投递设施接受这封信之前，或由它明确拒收。原始异常保留在 <see cref="Exception.InnerException"/>；
/// 无法归类的失败与取消不包成本异常，原样抛出。
/// </remarks>
public sealed class EmailDeliveryException : Exception
{
    /// <summary>创建发信失败异常。</summary>
    /// <param name="failureKind">失败类别。</param>
    /// <param name="message">面向运维的英文消息；不放收件人地址与凭据，消息会随异常对象进日志。</param>
    /// <param name="innerException">投递设施客户端抛出的原始异常。</param>
    public EmailDeliveryException(EmailDeliveryFailureKind failureKind, string message, Exception innerException)
        : base(message, innerException ?? throw new ArgumentNullException(nameof(innerException)))
    {
        FailureKind = failureKind;
    }

    /// <summary>失败类别。</summary>
    public EmailDeliveryFailureKind FailureKind { get; }
}
