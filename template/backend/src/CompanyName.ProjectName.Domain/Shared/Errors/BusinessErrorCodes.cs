namespace CompanyName.ProjectName.Domain.Shared.Errors;

/// <summary>
/// Stable API error codes. The first three digits are the HTTP status code.
/// </summary>
public static class BusinessErrorCodes
{
    public const int UsernameAlreadyExists = 40001;
    public const int EmailAlreadyInUse = 40002;
    public const int LocalPasswordNotConfigured = 40003;
    public const int CurrentPasswordIncorrect = 40004;
    public const int PasswordRequired = 40005;
    public const int PasswordHashRequired = 40006;
    public const int VerificationPasswordRequired = 40007;
    public const int EmailVerificationCodeRequired = 40008;
    public const int EmailVerificationCodeInvalid = 40009;
    public const int CaptchaInvalid = 40010;
    public const int EmailVerificationRateLimited = 40011;
    public const int ExternalProviderUnsupported = 40012;
    public const int OAuthTokenExchangeFailed = 40013;
    public const int OAuthTokenInvalid = 40014;
    public const int OAuthAccessTokenMissing = 40015;
    public const int OAuthUserInfoFailed = 40016;
    public const int OAuthProviderUserIdMissing = 40017;
    public const int GrantTypeUnsupported = 40020;
    public const int ClientIdRequired = 40030;
    public const int ClientIdAlreadyExists = 40031;
    public const int OpenApplicationCreationFailed = 40032;
    public const int ConfidentialClientRequired = 40033;
    public const int ApplicationTypeUnsupported = 40034;
    public const int ClientTypeUnsupported = 40035;
    public const int ConsentTypeUnsupported = 40036;
    public const int PkceRequired = 40037;
    public const int UriInvalid = 40038;
    public const int BuiltInAdminUpdateForbidden = 40050;
    public const int BuiltInAdminOperationForbidden = 40051;
    public const int BuiltInAdminDisableForbidden = 40052;
    public const int BuiltInAdminSelfDisableForbidden = 40053;
    public const int BuiltInAdminPasswordResetForbidden = 40054;
    public const int BuiltInAdminDeleteForbidden = 40055;
    public const int RoleRequired = 40056;
    public const int RoleNotFound = 40057;

    public const int InvalidCredentials = 40101;
    public const int UserDisabled = 40102;
    public const int UserLocked = 40103;

    public const int NotificationIdentityForbidden = 40301;

    public const int UserNotFound = 40401;
    public const int ExternalProviderNotConfigured = 40410;
    public const int ExternalProviderRedirectUriMissing = 40411;
    public const int OAuthConfigurationMissing = 40412;
    public const int OpenApplicationNotFound = 40420;
}
