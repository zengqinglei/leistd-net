using Leistd.Email.Abstractions;
using Leistd.Email.Smtp;
using Leistd.Email.Smtp.Options;
using Leistd.TestBase.Assertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Leistd.Email.Tests.Smtp;

/// <summary>
/// SMTP 发送器的注册面。
/// </summary>
public sealed class SmtpDependencyInjectionTests
{
    [Fact]
    public void Registration_resolves_the_smtp_sender()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddSmtpEmailSender(o =>
        {
            o.Host = "127.0.0.1";
            o.DefaultFromAddress = "noreply@example.com";
        });

        services.AssertResolvesTo<IEmailSender, SmtpEmailSender>();
    }

    // 实现类型注册一次、接口作别名转发：两次注册产生两个 SmtpEmailSender 时，
    // 每份各自持有自己的 SmtpClient 与日志，行为差异只在压测下才显形
    [Fact]
    public void Registration_is_idempotent()
        => ServiceCollectionAssertions.AssertIdempotent(services =>
        {
            services.AddLogging();
            services.AddSmtpEmailSender(o =>
            {
                o.Host = "127.0.0.1";
                o.DefaultFromAddress = "noreply@example.com";
            });
        });

    [Fact]
    public void The_interface_and_the_implementation_are_the_same_instance()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSmtpEmailSender(o =>
        {
            o.Host = "127.0.0.1";
            o.DefaultFromAddress = "noreply@example.com";
        });

        using var provider = services.BuildServiceProvider();

        Assert.Same(provider.GetRequiredService<SmtpEmailSender>(), provider.GetRequiredService<IEmailSender>());
    }

    [Fact]
    public void The_configuration_overload_binds_the_documented_section()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{SmtpOptions.SectionName}:Host"] = "smtp.internal",
                [$"{SmtpOptions.SectionName}:Port"] = "2525",
                [$"{SmtpOptions.SectionName}:DefaultFromAddress"] = "noreply@internal",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSmtpEmailSender(configuration);
        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<SmtpOptions>>().Value;

        Assert.Equal("smtp.internal", options.Host);
        Assert.Equal(2525, options.Port);
        Assert.Equal("noreply@internal", options.DefaultFromAddress);
    }

    // ValidateOnStart 没接上时，非法配置会一路走到第一次发送才失败
    [Fact]
    public void Invalid_options_fail_the_host_at_startup()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSmtpEmailSender(o => o.Host = string.Empty);

        using var provider = services.BuildServiceProvider();

        var startupValidators = provider.GetServices<IStartupValidator>().ToList();
        Assert.NotEmpty(startupValidators);
        var failure = Assert.Throws<OptionsValidationException>(() => startupValidators[0].Validate());
        Assert.Contains(SmtpOptions.SectionName, Assert.Single(failure.Failures), StringComparison.Ordinal);
    }

    [Fact]
    public void The_configuration_overload_rejects_a_null_configuration()
        => Assert.Throws<ArgumentNullException>(() => new ServiceCollection().AddSmtpEmailSender((IConfiguration)null!));
}
