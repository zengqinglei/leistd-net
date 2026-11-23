using System.Net;
using System.Net.Sockets;
using CompanyName.ProjectName.Api.Configuration;
using CompanyName.ProjectName.Application.Settings.Provider;
using Leistd.Email.Smtp;
using Leistd.Email.Smtp.Options;
using Leistd.Settings.Checks;
using Leistd.Settings.Definitions;
using Leistd.Settings.Exceptions;
using Leistd.Settings.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CompanyName.ProjectName.UnitTests.Api;

/// <summary>系统设置的 SMTP 测试：用这一次读到的存储值（缺项取部署配置）只连接与认证；没配主机不连，口令解不开不拿旧口令去测。</summary>
/// <remarks>连通与认证的分类由框架 <c>SmtpEmailSender.ProbeAsync</c> 的协议测试覆盖，这里只验本项目的取值与映射。</remarks>
public sealed class SmtpConnectionCheckTests
{
    [Fact]
    public async Task Without_a_host_nothing_is_contacted()
    {
        var outcome = await Check([]).ExecuteAsync(Context([]), CancellationToken.None);

        Assert.Equal(SettingCheckFailureCodes.NotConfigured, outcome.FailureCode);
    }

    // 存储里的端口优先于部署配置：本机监听一个端口，接受连接后立即关闭，连接落在它上面即说明用了存储值
    [Fact]
    public async Task Stored_values_win_over_the_deployment_configuration()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        // 接受后立刻断开：客户端读到连接关闭即判为连不上，不必等满问候语的读超时
        var accepted = Task.Run(async () =>
        {
            using var connection = await listener.AcceptTcpClientAsync();
        });

        var outcome = await Check(new() { [SettingConstant.Email.SmtpPort] = "1" }).ExecuteAsync(Context(new()
        {
            [SettingConstant.Email.SmtpHost] = "127.0.0.1",
            [SettingConstant.Email.SmtpPort] = port.ToString(System.Globalization.CultureInfo.InvariantCulture),
            [SettingConstant.Email.SmtpEnableSsl] = "false",
        }), CancellationToken.None);

        await accepted.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(SettingCheckFailureCodes.Unreachable, outcome.FailureCode);
    }

    [Fact]
    public async Task A_stored_password_that_cannot_be_decrypted_is_not_replaced_by_the_deployed_one()
    {
        var context = Context(new() { [SettingConstant.Email.SmtpHost] = "127.0.0.1" }, undecryptable: SettingConstant.Email.SmtpPassword);

        await Assert.ThrowsAsync<SettingDecryptionException>(
            () => Check(new() { [SettingConstant.Email.SmtpPassword] = "deployed" }).ExecuteAsync(context, CancellationToken.None));
    }

    private static SmtpConnectionCheck Check(Dictionary<string, string?> deployed)
        => new(
            new Baseline(deployed),
            new SmtpEmailSender(new Monitor(new SmtpOptions()), NullLogger<SmtpEmailSender>.Instance));

    private static SettingCheckContext Context(Dictionary<string, string?> stored, string? undecryptable = null)
        => new(SettingConstant.Email.SmtpHost, SettingScopes.Host, (name, _) => name == undecryptable
            ? Task.FromException<string?>(new SettingDecryptionException(name, [name], keyId: null, new InvalidOperationException()))
            : Task.FromResult(stored.GetValueOrDefault(name)));

    private sealed class Baseline(Dictionary<string, string?> values) : IHostSettingBaseline
    {
        public string? GetDeploymentValue(string settingName) => values.GetValueOrDefault(settingName);
    }

    private sealed class Monitor(SmtpOptions value) : IOptionsMonitor<SmtpOptions>
    {
        public SmtpOptions CurrentValue => value;

        public SmtpOptions Get(string? name) => value;

        public IDisposable? OnChange(Action<SmtpOptions, string?> listener) => null;
    }
}
