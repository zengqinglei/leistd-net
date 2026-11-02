using Leistd.Webhooks.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Leistd.Webhooks.Tests;

/// <summary>配置节绑定的非法值在解析选项时被拒绝，每条消息以实际配置节的键开头。</summary>
public sealed class WebhookOptionsValidationTests
{
    [Theory]
    [InlineData("RetrySchedule:0", "00:00:00")]
    [InlineData("RequestTimeout", "00:10:00")]
    [InlineData("DispatchInterval", "00:00:00")]
    [InlineData("AutoDisableAfter", "00:00:00")]
    [InlineData("SecretRotationOverlap", "31.00:00:00")]
    [InlineData("AllowedNetworks:0", "not-a-network")]
    public void An_invalid_value_fails_with_a_message_starting_with_its_key(string key, string value)
    {
        foreach (var section in new[] { WebhookOptions.SectionName, "Ops:Hooks" })
        {
            var failure = Assert.Throws<OptionsValidationException>(() => Resolve(section, new() { [$"{section}:{key}"] = value }));

            var message = Assert.Single(failure.Failures);
            Assert.StartsWith($"{section}:{key} ", message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void A_retry_schedule_longer_than_the_limit_is_rejected()
    {
        var settings = Enumerable.Range(0, WebhookOptionsValidator.MaxRetryCount + 1)
            .ToDictionary(i => $"{WebhookOptions.SectionName}:RetrySchedule:{i}", _ => (string?)"00:01:00");

        var failure = Assert.Throws<OptionsValidationException>(() => Resolve(WebhookOptions.SectionName, settings));

        Assert.StartsWith($"{WebhookOptions.SectionName}:RetrySchedule ", Assert.Single(failure.Failures), StringComparison.Ordinal);
    }

    /// <summary>配置文件里的退避表替换默认表，而不是接在默认表后面。</summary>
    [Fact]
    public void A_configured_retry_schedule_replaces_the_default()
    {
        var options = Resolve(WebhookOptions.SectionName, new()
        {
            [$"{WebhookOptions.SectionName}:RetrySchedule:0"] = "00:00:30",
            [$"{WebhookOptions.SectionName}:RetrySchedule:1"] = "00:02:00"
        });

        Assert.Equal([TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(2)], options.GetRetrySchedule());
    }

    private static WebhookOptions Resolve(string section, Dictionary<string, string?> settings)
    {
        var services = new ServiceCollection()
            .AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        services.AddWebhooks(configSectionPath: section);
        return services.BuildServiceProvider().GetRequiredService<IOptions<WebhookOptions>>().Value;
    }
}
