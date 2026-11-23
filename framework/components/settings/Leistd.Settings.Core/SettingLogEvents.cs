namespace Leistd.Settings;

/// <summary>设置组件记录的日志事件号，号段 4200–4299；告警按日志类别加事件号匹配。</summary>
public static class SettingLogEvents
{
    /// <summary>机密设置解不开（Error）：密钥丢失、密钥环未共享、应用名不同或密文被篡改。字段 <c>SettingName</c>、<c>KeyId</c>、<c>ErrorType</c>。</summary>
    public const int DecryptionFailed = 4201;

    /// <summary>宿主设置的新值让绑定的 Options 校验失败，整组沿用上一组（Error）。字段 <c>Failures</c>。</summary>
    public const int HostSettingsRejected = 4202;

    /// <summary>宿主设置这一轮没能应用，沿用当前生效的值、等下一轮重试（Warning），例如库尚未迁移。</summary>
    public const int HostSettingsNotApplied = 4203;

    /// <summary>当前上下文读不到宿主层，宿主设置没有应用（Warning）。</summary>
    public const int HostScopeUnreachable = 4204;
}
