namespace CompanyName.ProjectName.Domain.Users.Errors;

/// <summary>User 业务错误码。</summary>
public static class UserErrorCodes
{
    public const string AvatarInvalid = "User:AvatarInvalid";
    public const string AvatarTooLarge = "User:AvatarTooLarge";
    public const string EmailAlreadyUsed = "User:EmailAlreadyUsed";
    public const string EmailTaken = "User:EmailTaken";
    public const string ManageRolesRequired = "User:ManageRolesRequired";
    public const string NotFound = "User:NotFound";
    public const string RolesNotFound = "User:RolesNotFound";
    public const string SuperAdminDeleteForbidden = "User:SuperAdminDeleteForbidden";
    public const string SuperAdminDisableForbidden = "User:SuperAdminDisableForbidden";
    public const string SuperAdminDisableSelfForbidden = "User:SuperAdminDisableSelfForbidden";
    public const string SuperAdminOperationForbidden = "User:SuperAdminOperationForbidden";
    public const string SuperAdminResetPasswordForbidden = "User:SuperAdminResetPasswordForbidden";
    public const string SuperAdminUpdateForbidden = "User:SuperAdminUpdateForbidden";
    public const string UsernameTaken = "User:UsernameTaken";
}
