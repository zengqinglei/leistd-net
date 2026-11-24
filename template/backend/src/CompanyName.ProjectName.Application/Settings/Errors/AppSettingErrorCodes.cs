namespace CompanyName.ProjectName.Application.Settings.Errors;

/// <summary>项目设置用例的业务错误码；与设置组件的 Setting 前缀分离。</summary>
public static class AppSettingErrorCodes
{
#if (Email)
    public const string EmailAddressInvalid = "AppSetting:EmailAddressInvalid";
    public const string EmailVerificationKeyMissing = "AppSetting:EmailVerificationKeyMissing";
    // 测试邮件的失败原因：界面按码指明该查哪一项，所以每种原因一个码；归不了类的用 TestEmailFailed
    public const string TestEmailAuthRejected = "AppSetting:TestEmailAuthRejected";
    public const string TestEmailFailed = "AppSetting:TestEmailFailed";
    public const string TestEmailRejected = "AppSetting:TestEmailRejected";
    public const string TestEmailUnreachable = "AppSetting:TestEmailUnreachable";
#endif
#if (IncludeNotifications)
    public const string NotificationRetentionOrder = "AppSetting:NotificationRetentionOrder";
#endif
    public const string TelemetryEndpointInvalid = "AppSetting:TelemetryEndpointInvalid";
    // 运维分组"测试"导出的失败原因：界面按码给出本地文案，所以每种原因一个码
    public const string TelemetryExportAuthRejected = "AppSetting:TelemetryExportAuthRejected";
    public const string TelemetryExportNotConfigured = "AppSetting:TelemetryExportNotConfigured";
    public const string TelemetryExportUnavailable = "AppSetting:TelemetryExportUnavailable";
    public const string TelemetryExportUnexpectedStatus = "AppSetting:TelemetryExportUnexpectedStatus";
    public const string TelemetryExportUnreachable = "AppSetting:TelemetryExportUnreachable";
    public const string TelemetryHeadersInvalid = "AppSetting:TelemetryHeadersInvalid";
    public const string TimeZoneInvalid = "AppSetting:TimeZoneInvalid";
}
