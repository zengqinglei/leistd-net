using CompanyName.ProjectName.Api.Configuration;
using CompanyName.ProjectName.Application.Settings.Provider;
using Leistd.Email.Smtp;
using Leistd.Email.Smtp.Options;
using Leistd.Settings.Checks;
using Leistd.Settings.Definitions;
using Leistd.Settings.Exceptions;
using Leistd.Settings.Resolution;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CompanyName.ProjectName.UnitTests.Api;

/// <summary>系统设置的 SMTP 测试：只连接与认证；没配主机不连，存着的口令解不开时不拿旧口令去测。</summary>
/// <remarks>连通与认证的分类由框架 <c>SmtpEmailSender.ProbeAsync</c> 的协议测试覆盖，这里只验本项目的取值与映射。</remarks>
public sealed class SmtpConnectionCheckTests
{
    private static readonly SettingCheckContext Context = new(SettingConstant.Email.SmtpHost, SettingScopes.Host);

    [Fact]
    public async Task Without_a_host_nothing_is_contacted()
    {
        var outcome = await Check(new SmtpOptions { Host = "" }).ExecuteAsync(Context, CancellationToken.None);

        Assert.Equal(SettingCheckFailureCodes.NotConfigured, outcome.FailureCode);
    }

    // 本机没有监听的端口：连接立刻被拒
    [Fact]
    public async Task A_server_that_refuses_the_connection_is_unreachable()
    {
        var outcome = await Check(new SmtpOptions { Host = "127.0.0.1", Port = 1, DefaultFromAddress = "noreply@example.com" })
            .ExecuteAsync(Context, CancellationToken.None);

        Assert.Equal(SettingCheckFailureCodes.Unreachable, outcome.FailureCode);
    }

    [Fact]
    public async Task A_stored_password_that_cannot_be_decrypted_is_not_replaced_by_the_deployed_one()
    {
        var check = Check(new SmtpOptions { Host = "127.0.0.1", Port = 1 }, undecryptable: SettingConstant.Email.SmtpPassword);

        await Assert.ThrowsAsync<SettingDecryptionException>(() => check.ExecuteAsync(Context, CancellationToken.None));
    }

    private static SmtpConnectionCheck Check(SmtpOptions options, string? undecryptable = null)
    {
        var monitor = new Monitor(options);
        return new SmtpConnectionCheck(
            new Settings(undecryptable),
            new SmtpEmailSender(monitor, NullLogger<SmtpEmailSender>.Instance),
            monitor);
    }

    private sealed class Settings(string? undecryptable) : ISettingProvider
    {
        public Task<string?> GetOrNullAsync(string name, CancellationToken cancellationToken = default)
            => name == undecryptable
                ? Task.FromException<string?>(new SettingDecryptionException(name, [name], keyId: null, new InvalidOperationException()))
                : Task.FromResult<string?>(null);

        public Task<string?> GetOrNullForUserAsync(string name, string userId, CancellationToken cancellationToken = default)
            => GetOrNullAsync(name, cancellationToken);

        public Task<T?> GetAsync<T>(string name, CancellationToken cancellationToken = default)
            => Task.FromResult<T?>(default);

        public Task<IReadOnlyDictionary<string, string?>> GetAllAsync(
            bool visibleToClientsOnly = false,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyDictionary<string, string?>>(new Dictionary<string, string?>());
    }

    private sealed class Monitor(SmtpOptions value) : IOptionsMonitor<SmtpOptions>
    {
        public SmtpOptions CurrentValue => value;

        public SmtpOptions Get(string? name) => value;

        public IDisposable? OnChange(Action<SmtpOptions, string?> listener) => null;
    }
}
