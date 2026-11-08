using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Leistd.EventBus.RabbitMQ.Options;

internal sealed class RabbitMqEventTransportOptionsValidator(string path, IConfiguration configuration, IHostEnvironment environment)
    : IValidateOptions<RabbitMqEventTransportOptions>
{
    public ValidateOptionsResult Validate(string? name, RabbitMqEventTransportOptions options)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(options.ConnectionStringName)) errors.Add($"{path}:ConnectionStringName is required.");
        else if (!Uri.TryCreate(configuration.GetConnectionString(options.ConnectionStringName), UriKind.Absolute, out var uri)
            || uri.Scheme is not ("amqp" or "amqps")) errors.Add($"ConnectionStrings:{options.ConnectionStringName} must be an amqp or amqps URI.");
        if (string.IsNullOrWhiteSpace(options.Exchange)) errors.Add($"{path}:Exchange is required.");
        if (string.IsNullOrWhiteSpace(options.QueueName ?? environment.ApplicationName)) errors.Add($"{path}:QueueName or the host application name is required.");
        if (options.PrefetchCount == 0) errors.Add($"{path}:PrefetchCount must be positive.");
        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}

internal sealed record RabbitMqEventTransportRegistration(string Path);
