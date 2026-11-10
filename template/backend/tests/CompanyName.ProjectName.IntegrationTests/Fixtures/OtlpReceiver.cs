using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace CompanyName.ProjectName.IntegrationTests.Fixtures;

/// <summary>本机 OTLP/HTTP 接收端：记录路径、请求头与原始正文（protobuf），回空的 200。</summary>
internal sealed class OtlpReceiver : IDisposable
{
    private readonly HttpListener listener = new();
    private readonly ConcurrentQueue<OtlpRequest> requests = new();
    private readonly bool redirectSignals;

    /// <summary>启动接收端。</summary>
    /// <param name="redirectSignals">信号路径一律回 302 到 <c>/login</c>，模拟 Collector 前的登录网关。</param>
    public OtlpReceiver(bool redirectSignals = false)
    {
        this.redirectSignals = redirectSignals;
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        BaseAddress = $"http://127.0.0.1:{port}";
        listener.Prefixes.Add(BaseAddress + "/");
        listener.Start();
        _ = Task.Run(AcceptAsync);
    }

    public string BaseAddress { get; }

    public IReadOnlyList<OtlpRequest> Requests => requests.ToArray();

    public void Dispose() => listener.Close();

    private async Task AcceptAsync()
    {
        while (listener.IsListening)
        {
            HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync();
            }
            catch (Exception error) when (error is HttpListenerException or ObjectDisposedException or InvalidOperationException)
            {
                return;
            }

            var request = context.Request;
            using var body = new MemoryStream();
            await request.InputStream.CopyToAsync(body);
            var headers = request.Headers.AllKeys.Where(key => key is not null).ToDictionary(
                key => key!, key => request.Headers.GetValues(key) ?? [], StringComparer.OrdinalIgnoreCase);
            requests.Enqueue(new OtlpRequest(request.Url!.AbsolutePath, request.ContentType, headers, body.ToArray()));
            if (redirectSignals && request.Url!.AbsolutePath.StartsWith("/v1/", StringComparison.Ordinal))
            {
                context.Response.StatusCode = 302;
                context.Response.RedirectLocation = "/login";
            }
            else
            {
                context.Response.StatusCode = 200;
            }

            context.Response.Close();
        }
    }
}

/// <summary>接收端收到的一次导出请求；<see cref="Body"/> 是未解码的 protobuf，字符串字段以 UTF-8 原样出现。</summary>
internal sealed record OtlpRequest(string Path, string? ContentType, IReadOnlyDictionary<string, string[]> Headers, byte[] Body)
{
    public string[] Header(string name) => Headers.TryGetValue(name, out var values) ? values : [];
}
