namespace Leistd.Email.Exceptions;

/// <summary>发信失败的类别；调用方据此提示该检查哪一组发信参数。</summary>
public enum EmailDeliveryFailureKind
{
    /// <summary>
    /// 连不上投递设施或它此刻无法服务：地址或端口不通、TLS 握手失败、服务器不支持所要求的加密方式、
    /// 连接或认证阶段超时与断开，或服务器暂时拒绝。邮件尚未提交，可以重试。
    /// </summary>
    Unreachable,

    /// <summary>投递设施拒绝了登录凭据，或不支持认证。</summary>
    AuthenticationRejected,

    /// <summary>投递设施拒收这封信：发件地址、收件人或邮件本身不被接受。</summary>
    DeliveryRejected,
}
