using Leistd.Tracing.Options;
using Leistd.Tracing.Services;
using Microsoft.Extensions.Options;
using Leistd.Tracing.Abstractions;

namespace Leistd.Tracing.HttpClient.Handlers;

/// <summary>
/// 出站链路标识处理器：把当前 TraceId 写入 <c>HttpClient</c> 请求头，向下游透传。
/// </summary>
public class CorrelationIdDelegatingHandler(
    ICorrelationIdProvider correlationIdProvider,
    IOptionsMonitor<CorrelationIdOptions> optionsMonitor) : DelegatingHandler
{
    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var options = optionsMonitor.CurrentValue;
        if (!options.Enabled)
        {
            return await base.SendAsync(request, cancellationToken);
        }

        var correlationId = correlationIdProvider.Get();
        if (!string.IsNullOrEmpty(correlationId))
        {
            foreach (var headerName in options.HeaderNames)
            {
                if (!request.Headers.Contains(headerName))
                {
                    request.Headers.Add(headerName, correlationId);
                }
            }
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
