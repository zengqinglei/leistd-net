namespace CompanyName.ProjectName.Application.Settings.Provider;

/// <summary>
/// 设置名称常量
/// </summary>
/// <remarks>
/// 设置名是跨前后端的字符串契约，集中在此避免两侧各写一份字面量后漂移。
/// </remarks>
public static class SettingConstant
{
    /// <summary>
    /// 设置分组标识。
    /// </summary>
    /// <remarks>
    /// 分组决定设置页左侧的分类，词条键按 <c>SettingGroup:{标识}</c> 推导。
    /// 与设置名一样是跨前后端的字符串契约：界面按标识选中分类，因此它不能随文案改动。
    /// </remarks>
    public static class Groups
    {
        /// <summary>界面显示：语言、时区这类"怎么呈现"的偏好。</summary>
        public const string Display = "Display";

        /// <summary>日志：进程级的运维开关。</summary>
        public const string Logging = "Logging";

#if (LocalIdentity)
        /// <summary>注册与验证：租户各自的注册策略。</summary>
        public const string Registration = "Registration";

#endif
        /// <summary>没写分组的设置的归处，避免它落在界面之外。</summary>
        public const string Other = "Other";
    }

    /// <summary>界面显示偏好。</summary>
    public static class Display
    {
#if (IncludeLocalization)
        /// <summary>界面语言（culture 名，如 <c>zh-CN</c>）。</summary>
        public const string Language = "Display.Language";

#endif
        /// <summary>展示时区（IANA 名，如 <c>Asia/Shanghai</c>）。时间一律以 UTC 存储，仅展示时换算。</summary>
        public const string TimeZone = "Display.TimeZone";
    }

    /// <summary>
    /// 日志开关。
    /// </summary>
    /// <remarks>
    /// 都是<b>进程级</b>（<c>SettingScopes.Host</c>）：一个进程只有一个 logger，
    /// 按租户各存一份根本无从生效。改完立即对本进程生效，其它实例在下一次刷新周期跟上
    /// （见 <c>LoggingSettingRefresher</c>）。
    /// </remarks>
    public static class Logging
    {
        /// <summary>全局最小日志级别。</summary>
        public const string MinimumLevel = "Logging.MinimumLevel";

        /// <summary>正常完成的请求记成哪一级；调高即等于关掉请求日志。</summary>
        public const string RequestLevel = "Logging.RequestLevel";

        /// <summary>
        /// 合法的日志级别取值，与 Serilog 的 <c>LogEventLevel</c> 一致。
        /// </summary>
        /// <remarks>
        /// 定义的默认值、写入校验与界面候选项都读这一份：分开写几份时，
        /// 加一档只改了一处，就会变成"选得出却写不进"或者反过来。
        /// </remarks>
        public static readonly IReadOnlyList<string> Levels =
            ["Verbose", "Debug", "Information", "Warning", "Error", "Fatal"];
    }
#if (LocalIdentity)

    /// <summary>
    /// 注册与验证策略。
    /// </summary>
    /// <remarks>
    /// 租户级：每个租户可以有自己的注册策略。代码默认值取自 <c>appsettings</c> 的
    /// <c>UserRegistration</c> 段——配置仍是部署基线，设置只是租户级覆盖，
    /// 不另立一份真相。
    /// </remarks>
    public static class Registration
    {
        /// <summary>是否要求邮箱验证。</summary>
        public const string EnableEmailVerification = "Registration.EnableEmailVerification";

        /// <summary>图形验证码有效期（分钟）。</summary>
        public const string CaptchaExpiryMinutes = "Registration.CaptchaExpiryMinutes";

        /// <summary>邮箱验证码有效期（分钟）。</summary>
        public const string EmailCodeExpiryMinutes = "Registration.EmailCodeExpiryMinutes";

        /// <summary>发送邮箱验证码的最小间隔（秒）。</summary>
        public const string EmailCodeSendIntervalSeconds = "Registration.EmailCodeSendIntervalSeconds";

        /// <summary>单个验证挑战允许的最大错误次数。</summary>
        public const string EmailCodeMaxAttempts = "Registration.EmailCodeMaxAttempts";

        /// <summary>
        /// 数值型注册设置的取值区间。
        /// </summary>
        /// <remarks>
        /// 写入校验与下发给界面的 <c>minimum</c>/<c>maximum</c> 读同一份：分开写两份时，
        /// 界面允许的和服务端允许的会各走一边，表现成"输入框让填、保存被拒"。
        /// 区间本身与 <c>UserRegistrationOptions</c> 上的 <c>[Range]</c> 一致。
        /// </remarks>
        public static readonly IReadOnlyDictionary<string, (int Minimum, int Maximum)> Ranges =
            new Dictionary<string, (int, int)>(StringComparer.Ordinal)
            {
                [CaptchaExpiryMinutes] = (1, 60),
                [EmailCodeExpiryMinutes] = (1, 60),
                [EmailCodeSendIntervalSeconds] = (1, 3600),
                [EmailCodeMaxAttempts] = (1, 20)
            };
    }
#endif
}
