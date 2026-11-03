using System.Net;
using System.Net.Sockets;
using Leistd.Webhooks.Options;
using Microsoft.Extensions.Options;

namespace Leistd.Webhooks.Delivery;

// 内网拦截的唯一检查点：挂在 SocketsHttpHandler.ConnectCallback 上，按连接时实际解析出的地址判断，
// 防住"建订阅时是公网、投递时被解析到内网"的 DNS 重绑定。解析结果里只要有一个受限地址就整体拒绝。
internal sealed class WebhookNetworkGuard(IOptionsMonitor<WebhookOptions> options, string configSectionPath)
{
    private readonly string _allowedNetworksKey = configSectionPath + ":" + nameof(WebhookOptions.AllowedNetworks);

    public async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        var endpoint = context.DnsEndPoint;
        var addresses = await Dns.GetHostAddressesAsync(endpoint.Host, cancellationToken).ConfigureAwait(false);
        if (addresses.Length == 0)
        {
            throw new IOException($"Webhook host '{endpoint.Host}' did not resolve to any address.");
        }

        var allowed = ParseAllowedNetworks();
        foreach (var address in addresses)
        {
            if (IsRestricted(address) && !IsAllowed(address, allowed))
            {
                throw new IOException(
                    $"Webhook host '{endpoint.Host}' resolves to {address}, which is a private, loopback, link-local or unspecified address; "
                    + $"add its network to {_allowedNetworksKey} to allow it.");
            }
        }

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(addresses, endpoint.Port, cancellationToken).ConfigureAwait(false);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    // 私网（RFC 1918、IPv6 唯一本地与站点本地）、回环、链路本地、未指定地址（含 0.0.0.0/8）。
    internal static bool IsRestricted(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            return bytes[0] switch
            {
                0 or 10 => true,
                172 => bytes[1] is >= 16 and <= 31,
                192 => bytes[1] == 168,
                169 => bytes[1] == 254,
                _ => false
            };
        }

        return address.Equals(IPAddress.IPv6Any)
            || address.IsIPv6LinkLocal
            || address.IsIPv6SiteLocal
            || address.IsIPv6UniqueLocal;
    }

    private static bool IsAllowed(IPAddress address, List<IPNetwork> allowed)
    {
        var mapped = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
        return allowed.Exists(network => network.Contains(address) || network.Contains(mapped));
    }

    // 每次连接读当前值：配置热更新后新连接立即按新清单判断；格式已由选项验证器在启动期拒绝。
    private List<IPNetwork> ParseAllowedNetworks()
        => options.CurrentValue.AllowedNetworks.Select(network => IPNetwork.Parse(network)).ToList();
}
