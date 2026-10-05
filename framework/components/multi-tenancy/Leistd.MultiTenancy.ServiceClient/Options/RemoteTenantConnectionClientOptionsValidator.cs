using Microsoft.Extensions.Options;

namespace Leistd.MultiTenancy.ServiceClient.Options;

// 回源全用相对地址：缺 BaseAddress 时组合照常成功，要到首个租户请求才以不带键名的 URI 错误暴露。
// 只约束本存储自己的客户端，ServiceClient 通用选项仍允许留空（只用绝对地址的客户端存在）。
internal sealed class RemoteTenantConnectionClientOptionsValidator(string configSectionPath)
    : IValidateOptions<RemoteTenantConnectionClientOptions>
{
    public ValidateOptionsResult Validate(string? name, RemoteTenantConnectionClientOptions options)
        => Uri.TryCreate(options.BaseAddress, UriKind.Absolute, out _)
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                $"{configSectionPath}:{nameof(RemoteTenantConnectionClientOptions.BaseAddress)} is required by the remote " +
                "tenant connection store and must be an absolute URI of the control plane.");
}
