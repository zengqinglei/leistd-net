using Leistd.EventBus.Distributed.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Leistd.EventBus.Distributed.Options;

internal sealed class DistributedEventOptionsValidator(
    string configSectionPath,
    IServiceProviderIsService services) : IValidateOptions<DistributedEventOptions>
{
    public ValidateOptionsResult Validate(string? name, DistributedEventOptions options)
    {
        var failures = new List<string>();
        void Require(bool valid, string key, string message)
        {
            if (!valid) failures.Add($"{configSectionPath}:{key} {message}");
        }

        Require(options.RetryBaseDelay > TimeSpan.Zero, nameof(options.RetryBaseDelay), "must be greater than zero.");
        Require(options.RetryMaxDelay >= options.RetryBaseDelay, nameof(options.RetryMaxDelay), "must be at least RetryBaseDelay.");
        Require(options.DeadLetterAfter > TimeSpan.Zero, nameof(options.DeadLetterAfter), "must be greater than zero.");
        Require(options.MaxAttempts > 0, nameof(options.MaxAttempts), "must be greater than zero.");
        Require(options.DispatchInterval > TimeSpan.Zero, nameof(options.DispatchInterval), "must be greater than zero.");
        Require(options.BacklogWarningAfter > TimeSpan.Zero, nameof(options.BacklogWarningAfter), "must be greater than zero.");
        Require(options.Outbox is not null, nameof(options.Outbox), "must not be null.");
        Require(options.Inbox is not null, nameof(options.Inbox), "must not be null.");
        Require(options.TestEvent is not null, nameof(options.TestEvent), "must not be null.");
        if (options.Outbox is not null && options.Inbox is not null && !services.IsService(typeof(IDistributedEventTransport)))
        {
            Require(!options.Outbox.DispatchEnabled, "Outbox:DispatchEnabled", "requires an IDistributedEventTransport registration.");
            Require(!options.Inbox.ReceiveEnabled, "Inbox:ReceiveEnabled", "requires an IDistributedEventTransport registration.");
        }
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}

internal sealed record DistributedEventOptionsRegistration(string ConfigSectionPath);
