using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Leistd.EventBus.Kafka.Options;

internal sealed class KafkaEventTransportOptionsValidator(string path, IConfiguration configuration, IHostEnvironment environment)
    : IValidateOptions<KafkaEventTransportOptions>
{
    public ValidateOptionsResult Validate(string? name, KafkaEventTransportOptions options)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(options.BootstrapServersConnectionStringName)) errors.Add($"{path}:BootstrapServersConnectionStringName is required.");
        else if (string.IsNullOrWhiteSpace(configuration.GetConnectionString(options.BootstrapServersConnectionStringName)))
            errors.Add($"ConnectionStrings:{options.BootstrapServersConnectionStringName} is required.");
        if (string.IsNullOrWhiteSpace(options.GroupId ?? environment.ApplicationName)) errors.Add($"{path}:GroupId or the host application name is required.");
        if (options.TopicPrefix is null || options.TopicPrefix.Length > 248 || options.TopicPrefix.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-')))
            errors.Add($"{path}:TopicPrefix must contain only valid Kafka topic characters and leave room for an event name.");
        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
internal sealed record KafkaEventTransportRegistration(string Path);
