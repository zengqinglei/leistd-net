using System.Net;
using Leistd.Webhooks.Delivery;
using Xunit;

namespace Leistd.Webhooks.Tests;

/// <summary>连接期拦截的地址范围：私网、回环、链路本地与未指定地址受限，公网放行；IPv4 映射的 IPv6 地址按 IPv4 判断。</summary>
public sealed class WebhookNetworkGuardTests
{
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("10.1.2.3")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.255")]
    [InlineData("192.168.1.1")]
    [InlineData("169.254.169.254")]
    [InlineData("0.0.0.0")]
    [InlineData("::1")]
    [InlineData("::")]
    [InlineData("fe80::1")]
    [InlineData("fd00::1")]
    [InlineData("::ffff:127.0.0.1")]
    [InlineData("::ffff:10.0.0.1")]
    public void Private_loopback_link_local_and_unspecified_addresses_are_restricted(string address)
        => Assert.True(WebhookNetworkGuard.IsRestricted(IPAddress.Parse(address)));

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("172.32.0.1")]
    [InlineData("192.169.0.1")]
    [InlineData("2001:4860:4860::8888")]
    [InlineData("::ffff:8.8.8.8")]
    public void Public_addresses_are_not_restricted(string address)
        => Assert.False(WebhookNetworkGuard.IsRestricted(IPAddress.Parse(address)));
}
