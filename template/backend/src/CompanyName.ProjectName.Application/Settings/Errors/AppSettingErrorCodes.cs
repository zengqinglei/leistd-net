namespace CompanyName.ProjectName.Application.Settings.Errors;

/// <summary>项目设置用例的业务错误码；与设置组件的 Setting 前缀分离。</summary>
public static class AppSettingErrorCodes
{
    public const string EmailAddressInvalid = "AppSetting:EmailAddressInvalid";
    public const string EmailVerificationKeyMissing = "AppSetting:EmailVerificationKeyMissing";
    public const string ManagePermissionRequired = "AppSetting:ManagePermissionRequired";
    public const string TestEmailFailed = "AppSetting:TestEmailFailed";
    public const string TestEmailHostOnly = "AppSetting:TestEmailHostOnly";
    public const string TimeZoneInvalid = "AppSetting:TimeZoneInvalid";
}
