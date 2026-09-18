#if (LocalIdentity)
#if (IncludeNotifications)
using CompanyName.ProjectName.Application.Notifications;

#endif
#endif
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

        /// <summary>运维：进程怎么跑的开关（日志级别等），多为进程级。</summary>
        public const string Operations = "Operations";

        /// <summary>审计：操作记录保留多久，进程级。</summary>
        public const string Audit = "Audit";

#if (LocalIdentity)
        /// <summary>注册与验证：租户各自的注册策略。</summary>
        public const string Registration = "Registration";

        /// <summary>登录安全：租户各自的登录失败锁定等策略。</summary>
        public const string Security = "Security";

        /// <summary>邮件发送：进程级的 SMTP 参数。</summary>
        public const string Email = "Email";

#endif
#if (IncludeNotifications)
        /// <summary>通知偏好：每个人各自决定哪类通知经哪个渠道收。</summary>
        public const string Notifications = "Notifications";

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

    /// <summary>
    /// 操作记录保留期。
    /// </summary>
    /// <remarks>
    /// <b>进程级</b>：归档由一个后台任务跨租户统一执行，按租户各存一份无从生效。
    /// 设置值经宿主级设置配置源覆盖 <c>appsettings</c> 的 <c>OperationRecordRetention</c> 段，代码默认值就是该段的部署基线；
    /// 归档任务每轮取 Options 的当前值，下一轮即生效。执行时刻与批大小是部署调优参数，仍只在配置里。
    /// </remarks>
    public static class Audit
    {
        /// <summary>是否把超出保留期的操作记录搬入归档表。</summary>
        public const string RetentionEnabled = "Audit.RetentionEnabled";

        /// <summary>保留天数：早于"当前时刻减去该天数"的记录会被搬入归档表。</summary>
        public const string RetentionDays = "Audit.RetentionDays";

        /// <summary>数值型审计设置的取值区间，与 <c>OperationRecordRetentionOptions</c> 上的 <c>[Range]</c> 一致——设置值经配置源进入该 Options，超出区间会让它校验失败。</summary>
        public static readonly IReadOnlyDictionary<string, (int Minimum, int Maximum)> Ranges =
            new Dictionary<string, (int, int)>(StringComparer.Ordinal)
            {
                [RetentionDays] = (30, 3650)
            };
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

    /// <summary>
    /// 登录安全策略（租户级）。
    /// </summary>
    /// <remarks>代码默认值即推荐值，不另设配置节：它们不是部署环境相关的参数。</remarks>
    public static class Security
    {
        /// <summary>连续登录失败多少次后锁定账号；0 表示不锁定。</summary>
        public const string LockoutMaxFailedAttempts = "Security.LockoutMaxFailedAttempts";

        /// <summary>登录失败锁定的时长（分钟）。</summary>
        public const string LockoutDurationMinutes = "Security.LockoutDurationMinutes";

        /// <summary>是否要求所有人启用两步验证。</summary>
        public const string RequireTwoFactor = "Security.RequireTwoFactor";

        /// <summary>连续失败次数的默认阈值。</summary>
        public const int DefaultLockoutMaxFailedAttempts = 5;

        /// <summary>锁定时长的默认值（分钟）。</summary>
        public const int DefaultLockoutDurationMinutes = 15;

        /// <summary>数值型登录安全设置的取值区间，写入校验与界面上下界同源。</summary>
        public static readonly IReadOnlyDictionary<string, (int Minimum, int Maximum)> Ranges =
            new Dictionary<string, (int, int)>(StringComparer.Ordinal)
            {
                [LockoutMaxFailedAttempts] = (0, 100),
                [LockoutDurationMinutes] = (1, 1440)
            };
    }

    /// <summary>
    /// 发信参数（SMTP）。
    /// </summary>
    /// <remarks>
    /// <para><b>进程级</b>（<c>SettingScopes.Host</c>）：发信通道是整个部署共用的，不按租户各配一套。</para>
    /// <para>设置值经宿主级设置配置源覆盖配置文件的 <c>Leistd:Email:Smtp</c> 节，发信端照常读 <c>IOptionsMonitor&lt;SmtpOptions&gt;</c>；
    /// 代码默认值是该节的部署基线：配置给基线，设置表给运行期覆盖，清除覆盖值即回到基线。
    /// 口令例外——它是机密设置，没有默认值，未设置时就是配置里的口令。</para>
    /// </remarks>
    public static class Email
    {
        /// <summary>SMTP 主机。</summary>
        public const string SmtpHost = "Email.SmtpHost";

        /// <summary>SMTP 端口。</summary>
        public const string SmtpPort = "Email.SmtpPort";

        /// <summary>是否启用传输层加密（465 用隐式 TLS，其余端口用 STARTTLS）。</summary>
        public const string SmtpEnableSsl = "Email.SmtpEnableSsl";

        /// <summary>登录用户名；留空表示匿名投递。</summary>
        public const string SmtpUsername = "Email.SmtpUsername";

        /// <summary>登录口令。机密设置：加密落库、只写不读。</summary>
        public const string SmtpPassword = "Email.SmtpPassword";

        /// <summary>默认发件地址。</summary>
        public const string DefaultFromAddress = "Email.DefaultFromAddress";

        /// <summary>默认发件显示名。</summary>
        public const string DefaultFromName = "Email.DefaultFromName";

        /// <summary>数值型发信设置的取值区间。</summary>
        public static readonly IReadOnlyDictionary<string, (int Minimum, int Maximum)> Ranges =
            new Dictionary<string, (int, int)>(StringComparer.Ordinal)
            {
                [SmtpPort] = (1, 65535)
            };
    }

#if (IncludeNotifications)
    /// <summary>
    /// 通知偏好（用户级），命名为 <c>Notifications.{通知类别}.{渠道}</c>。
    /// </summary>
    /// <remarks>
    /// 类别即通知的 <c>Type</c>，渠道即 <c>INotificationChannel.Name</c>：投递过滤器按这两者拼出设置名去查，
    /// 没有对应设置的组合一律投递——新加一个类别不会因为忘了定义偏好而收不到。
    /// 安全提醒的站内通知不可关闭，因此没有 <c>Notifications.Security.InApp</c>。
    /// </remarks>
    public static class Notifications
    {
        private const string Prefix = "Notifications";

        /// <summary>安全提醒是否也发邮件（只发到已验证的邮箱）。</summary>
        public const string SecurityEmail = $"{Prefix}.{AppNotificationTypes.Security}.{AppNotificationChannels.Email}";

        /// <summary>系统通知是否在站内接收。</summary>
        public const string SystemInApp = $"{Prefix}.{AppNotificationTypes.System}.{AppNotificationChannels.InApp}";

        /// <summary>系统通知是否也发邮件。</summary>
        public const string SystemEmail = $"{Prefix}.{AppNotificationTypes.System}.{AppNotificationChannels.Email}";

        /// <summary>某类通知经某渠道的偏好设置名。</summary>
        public static string NameOf(string type, string channel) => $"{Prefix}.{type}.{channel}";
    }

#endif
#endif
    /// <summary>
    /// 布尔型设置（值只能是 <c>true</c> / <c>false</c>），写入校验与界面控件都读它。
    /// </summary>
    /// <remarks>
    /// 界面不另列清单、按下发的标志渲染开关：前端各记一份的话，漏登记的布尔设置会渲染成文本框，
    /// 管理员得手打 <c>true</c>。
    /// </remarks>
    public static readonly IReadOnlySet<string> BooleanSettings = new HashSet<string>(StringComparer.Ordinal)
    {
        Audit.RetentionEnabled,
#if (LocalIdentity)
        Registration.EnableEmailVerification,
        Security.RequireTwoFactor,
        Email.SmtpEnableSsl,
#if (IncludeNotifications)
        Notifications.SecurityEmail,
        Notifications.SystemInApp,
        Notifications.SystemEmail,
#endif
#endif
    };

    /// <summary>
    /// 全部数值型设置的取值区间，写入校验与下发给界面的上下界都读它。
    /// </summary>
    /// <remarks>各组的区间仍写在各自组里，这里只做汇总，新增一组时在这里并进来。</remarks>
    public static readonly IReadOnlyDictionary<string, (int Minimum, int Maximum)> NumericRanges =
#if (LocalIdentity)
        Audit.Ranges.Concat(Registration.Ranges).Concat(Security.Ranges).Concat(Email.Ranges)
            .ToDictionary(StringComparer.Ordinal);
#else
        Audit.Ranges.ToDictionary(StringComparer.Ordinal);
#endif
}
