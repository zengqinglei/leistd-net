namespace Leistd.Webhooks.Delivery;

/// <summary>发出一次 webhook 请求，组件唯一的替换点。</summary>
/// <remarks>
/// <para>默认实现按 Standard Webhooks v1 签名后 POST，超时取 <c>Leistd:Webhooks:RequestTimeout</c>，不跟随重定向，
/// 连接时拒绝不在 <c>Leistd:Webhooks:AllowedNetworks</c> 内的私网、回环与链路本地地址。可替换为托管投递服务。</para>
/// <para>只负责发送一次并如实报告结果；重试、退避、停用与记录由组件按结果处理，实现不自行重试。</para>
/// <para>网络失败、超时与被拦截的地址不抛异常，以 <see cref="WebhookSendResult.Error"/> 报告；取消时抛出
/// <see cref="OperationCanceledException"/>。</para>
/// </remarks>
public interface IWebhookSender
{
    /// <summary>发送一次请求。</summary>
    /// <param name="request">请求内容。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>本次尝试的结果。</returns>
    Task<WebhookSendResult> SendAsync(WebhookSendRequest request, CancellationToken cancellationToken = default);
}
