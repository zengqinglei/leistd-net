namespace Leistd.Webhooks.Options;

/// <summary>webhook 投递的部署配置。配置节 <c>Leistd:Webhooks</c>。</summary>
/// <remarks>
/// 全部有可直接上线的默认值；这些是协议参数与信任边界，归部署配置，不进设置页。
/// 保留期在子节 <c>Retention</c>，由 EF 包的 <c>AddWebhookRetention</c> 绑定。
/// </remarks>
public sealed class WebhookOptions
{
    /// <summary>配置节路径。</summary>
    public const string SectionName = "Leistd:Webhooks";

    /// <summary>默认退避表：5 秒、5 分钟、30 分钟、2 小时、5 小时、10 小时、10 小时，共约 27 小时。</summary>
    public static IReadOnlyList<TimeSpan> DefaultRetrySchedule { get; } =
    [
        TimeSpan.FromSeconds(5),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(30),
        TimeSpan.FromHours(2),
        TimeSpan.FromHours(5),
        TimeSpan.FromHours(10),
        TimeSpan.FromHours(10)
    ];

    /// <summary>首发失败后的重试间隔，第 i 次自动重试取第 i 项，实际时刻带 ±10% 抖动；
    /// 为 <see langword="null"/>（默认）时取 <see cref="DefaultRetrySchedule"/>，为空表时首发失败即失败。</summary>
    /// <remarks>
    /// 默认值不写在属性上：配置绑定对已有元素的集合是追加而不是替换，配置文件里的表会接在默认表后面。
    /// 读取生效值用 <see cref="GetRetrySchedule"/>。
    /// </remarks>
    public IReadOnlyList<TimeSpan>? RetrySchedule { get; set; }

    /// <summary>单次请求超时（1 秒–5 分钟），默认 15 秒。</summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>兜底扫描间隔（1 秒–1 小时），默认 10 秒。</summary>
    public TimeSpan DispatchInterval { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>从首次失败起连续失败多久后自动停用订阅（须大于 0），默认 5 天。</summary>
    public TimeSpan AutoDisableAfter { get; set; } = TimeSpan.FromDays(5);

    /// <summary>放行的私网 CIDR（如 <c>10.0.0.0/8</c>、<c>::1/128</c>），默认为空：私网、回环与链路本地地址一律拒绝连接。</summary>
    public IList<string> AllowedNetworks { get; set; } = [];

    /// <summary>轮换密钥后旧密钥继续一并签名的时长（0–30 天），默认 1 天；为 0 时轮换即失效。</summary>
    public TimeSpan SecretRotationOverlap { get; set; } = TimeSpan.FromDays(1);

    /// <summary>取生效的退避表：<see cref="RetrySchedule"/>，未配置时为 <see cref="DefaultRetrySchedule"/>。</summary>
    public IReadOnlyList<TimeSpan> GetRetrySchedule() => RetrySchedule ?? DefaultRetrySchedule;
}
