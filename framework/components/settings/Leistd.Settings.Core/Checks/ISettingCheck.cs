using Leistd.Settings.Definitions;

namespace Leistd.Settings.Checks;

/// <summary>对一组设置做一次探测（例如用凭据调一次供应商），确认配置可用。</summary>
/// <remarks>
/// <para>经 <c>AddSettingCheck&lt;T&gt;</c> 登记，按作用域解析。探针经 <see cref="SettingCheckContext"/> 读本次测试那一层的值：
/// 不含个人覆盖，也不读进程里已生效的配置（另一副本可能尚未刷新）。被绑定到部署配置的宿主级设置用
/// <see cref="SettingCheckContext.GetStoredValueAsync"/> 取存储值，没有时按部署配置的当前值取（Hosting 包的 <c>IHostSettingBaseline</c>）。
/// 读到解不开的机密设置时不必自己处理，调用方归一为 <see cref="SettingCheckFailureCodes.SecretUnavailable"/>。</para>
/// <para>失败用 <see cref="SettingCheckOutcome.Fail"/> 返回失败码，不抛异常；码取 <see cref="SettingCheckFailureCodes"/> 的基线，
/// 或业务自己的稳定字符串。结果会保存并展示给管理员，不要把供应商的原始响应或设置值放进去。</para>
/// </remarks>
public interface ISettingCheck
{
    /// <summary>执行一次探测。</summary>
    /// <param name="context">本次测试的设置名与层级。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<SettingCheckOutcome> ExecuteAsync(SettingCheckContext context, CancellationToken cancellationToken);
}

/// <summary>一次测试的上下文。</summary>
/// <param name="settingName">登记测试的主设置名。</param>
/// <param name="scope">测试所在的层级：租户上下文为 <see cref="SettingScopes.Tenant"/>，进程级设置为 <see cref="SettingScopes.Host"/>。</param>
/// <param name="readStored">按设置名读本层的存储值（机密已解密），没有时为 <see langword="null"/>；由 <see cref="ISettingCheckService"/> 提供，单测可传自己的实现。</param>
/// <param name="defaultOf">按设置名取代码默认值；省略时视为没有默认值。</param>
public sealed class SettingCheckContext(
    string settingName,
    SettingScopes scope,
    Func<string, CancellationToken, Task<string?>> readStored,
    Func<string, string?>? defaultOf = null)
{
    /// <summary>登记测试的主设置名。</summary>
    public string SettingName { get; } = settingName;

    /// <summary>测试所在的层级。</summary>
    public SettingScopes Scope { get; } = scope;

    /// <summary>读一项设置在本层的存储值（机密设置已解密）；本层没有存值时为 <see langword="null"/>。</summary>
    /// <remarks>
    /// 不经个人覆盖：结果是全租户（或宿主）共享的。被绑定到部署配置的宿主级设置在没有存值时，
    /// 由宿主按部署配置的当前值取，不用代码默认值（它是启动时的快照）。解不开时抛 <see cref="Exceptions.SettingDecryptionException"/>。
    /// </remarks>
    /// <param name="name">设置名。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public Task<string?> GetStoredValueAsync(string name, CancellationToken cancellationToken = default)
        => readStored(name, cancellationToken);

    /// <summary>读一项设置在本层的值：存储值，没有时为代码默认值。适用于没有绑定部署配置的设置。</summary>
    /// <param name="name">设置名。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<string?> GetValueAsync(string name, CancellationToken cancellationToken = default)
        => await readStored(name, cancellationToken) ?? defaultOf?.Invoke(name);
}

/// <summary>一次测试的结论。</summary>
public sealed record SettingCheckOutcome
{
    private SettingCheckOutcome(bool passed, string? failureCode)
    {
        Passed = passed;
        FailureCode = failureCode;
    }

    /// <summary>是否通过。</summary>
    public bool Passed { get; }

    /// <summary>未通过时的失败码；通过时为 <see langword="null"/>。</summary>
    public string? FailureCode { get; }

    /// <summary>通过。</summary>
    public static SettingCheckOutcome Pass() => new(true, null);

    /// <summary>未通过。</summary>
    /// <param name="failureCode">稳定的失败码，界面按它取文案；见 <see cref="SettingCheckFailureCodes"/>。</param>
    public static SettingCheckOutcome Fail(string failureCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(failureCode);
        return new(false, failureCode);
    }
}

/// <summary>测试失败码的通用基线；业务可以另加自己的稳定字符串，界面对未知码走兜底文案。</summary>
public static class SettingCheckFailureCodes
{
    /// <summary>探测所需的设置没有配置。</summary>
    public const string NotConfigured = "not-configured";

    /// <summary>机密设置用当前密钥环解不开；由框架在探针读值时自动给出。</summary>
    public const string SecretUnavailable = "secret-unavailable";

    /// <summary>对方拒绝了凭据（如 401、403）。</summary>
    public const string AuthRejected = "auth-rejected";

    /// <summary>连不上对方（DNS、连接、TLS 或超时）。</summary>
    public const string Unreachable = "unreachable";

    /// <summary>被对方限流。</summary>
    public const string RateLimited = "rate-limited";

    /// <summary>对方的服务或账号已停用。</summary>
    public const string ServiceDisabled = "service-disabled";

    /// <summary>对方返回了无法归类的状态。</summary>
    public const string UnexpectedStatus = "unexpected-status";

    /// <summary>探针自身抛出了异常；详情只进日志。</summary>
    public const string UnexpectedError = "unexpected-error";
}
