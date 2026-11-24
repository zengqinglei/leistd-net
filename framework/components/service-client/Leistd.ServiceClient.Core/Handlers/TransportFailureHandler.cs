using Leistd.ExceptionHandling;
using Leistd.ServiceClient.Exceptions;
using Leistd.ServiceClient.Http;

namespace Leistd.ServiceClient.Handlers;

// 把传输层异常统一为带故障类别的 ServiceClientException，由调用方的异常管道记录并映射状态码。
// 不写日志（IHttpClientFactory 已记录调用摘要）；调用方主动取消与已规范化的异常原样抛出。
// 内层处理器抛出的 BusinessException（如令牌交换要求重新认证）已是本服务对调用方的语义，同样原样抛出。
internal sealed class TransportFailureHandler : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            return await base.SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when (
            ex is not (ServiceClientException or BusinessException) &&
            !(ex is OperationCanceledException && cancellationToken.IsCancellationRequested))
        {
            throw new ServiceClientException(
                $"{RequestDescription.Of(request)} invocation failed: {ex.Message}", ex,
                ServiceClientFailureClassifier.Classify(ex));
        }
    }
}
