using Microsoft.Extensions.Options;

namespace Leistd.ServiceClient.OAuth.Options;

// 凭据缺失在启动期就拒绝：留到运行期，表现是第一次跨服务调用失败，而那时调用链已经在业务流程里了。
internal sealed class ClientCredentialsOptionsValidator : IValidateOptions<ClientCredentialsOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, ClientCredentialsOptions options)
    {
        var client = string.IsNullOrEmpty(name) ? "(default)" : name;
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.ClientId))
        {
            failures.Add($"{DependencyInjection.ServiceAuthSectionName}:ClientId is required for service client '{client}'.");
        }

        if (string.IsNullOrWhiteSpace(options.ClientSecret))
        {
            failures.Add(
                $"{DependencyInjection.ServiceAuthSectionName}:ClientSecret is required for service client '{client}'. " +
                "It belongs in a secret store or environment variable, not in the configuration file.");
        }

        if (string.IsNullOrWhiteSpace(options.Authority) && string.IsNullOrWhiteSpace(options.TokenEndpoint))
        {
            failures.Add(
                $"{DependencyInjection.ServiceAuthSectionName}:Authority or TokenEndpoint is required for service client " +
                $"'{client}'; the token endpoint defaults to '{{Authority}}/connect/token'.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
