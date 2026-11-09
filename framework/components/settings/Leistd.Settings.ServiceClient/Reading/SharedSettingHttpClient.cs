using Leistd.Settings.ServiceClient.Options;
using Microsoft.Extensions.Options;

namespace Leistd.Settings.ServiceClient.Reading;

// 一个提供方的类型化客户端：命名客户端、基址与管道由服务客户端组件按 TOptions 装配
internal sealed class SharedSettingHttpClient<TOptions>(HttpClient httpClient, IOptions<TOptions> options)
    where TOptions : SharedSettingsClientOptions, new()
{
    public Task<HttpResponseMessage> GetAsync(string tenantSegment, string? etag, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"{options.Value.Path.Trim('/')}/{Uri.EscapeDataString(tenantSegment)}");
        if (etag is not null)
        {
            request.Headers.TryAddWithoutValidation("If-None-Match", etag);
        }

        return httpClient.SendAsync(request, cancellationToken);
    }
}
