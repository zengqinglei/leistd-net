namespace Leistd.ServiceClient.Http;

// 异常消息里的请求要素：方法与不含查询串、片段和用户信息的地址。
// 消息随异常对象进日志，查询串常带被查询人的标识，用户信息可能是凭据。
internal static class RequestDescription
{
    public static string Of(HttpRequestMessage? request) =>
        $"{request?.Method} {Address(request?.RequestUri)}";

    private static string Address(Uri? uri)
    {
        if (uri is null)
        {
            return string.Empty;
        }

        if (uri.IsAbsoluteUri)
        {
            return uri.GetComponents(UriComponents.SchemeAndServer | UriComponents.Path, UriFormat.UriEscaped);
        }

        var value = uri.OriginalString;
        var end = value.IndexOfAny(['?', '#']);
        return end < 0 ? value : value[..end];
    }
}
