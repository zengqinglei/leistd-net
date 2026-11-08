using Microsoft.Extensions.Options;

namespace Leistd.EventBus.EntityFrameworkCore.Options;

internal sealed class DistributedEventRetentionOptionsValidator(string path) : IValidateOptions<DistributedEventRetentionOptions>
{
    public string ConfigSectionPath { get; } = path;
    public ValidateOptionsResult Validate(string? name, DistributedEventRetentionOptions options)
        => options.Days is >= 1 and <= 3650 ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail($"{ConfigSectionPath}:Days must be between 1 and 3650.");
}
