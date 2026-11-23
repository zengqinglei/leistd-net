using Leistd.Settings.Definitions;

namespace Leistd.Settings.Checks;

/// <summary>对一组设置做一次探测（例如用凭据调一次供应商），确认配置可用。</summary>
/// <remarks>
/// <para>经 <c>AddSettingCheck&lt;T&gt;</c> 登记，按作用域解析。探针经 <see cref="Resolution.ISettingProvider"/> 读当前生效的值；
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
/// <param name="SettingName">登记测试的主设置名。</param>
/// <param name="Scope">测试所在的层级：租户上下文为 <see cref="SettingScopes.Tenant"/>，进程级设置为 <see cref="SettingScopes.Host"/>。</param>
public sealed record SettingCheckContext(string SettingName, SettingScopes Scope);

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
