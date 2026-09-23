namespace CompanyName.ProjectName.Application.OpenApplications.Errors;

/// <summary>OpenApp 业务错误码。</summary>
public static class OpenAppErrorCodes
{
    public const string ApplicationTypeUnsupported = "OpenApp:ApplicationTypeUnsupported";
    public const string ClientIdRequired = "OpenApp:ClientIdRequired";
    public const string ClientIdTaken = "OpenApp:ClientIdTaken";
    public const string ClientTypeUnsupported = "OpenApp:ClientTypeUnsupported";
    public const string ConsentTypeUnsupported = "OpenApp:ConsentTypeUnsupported";
    public const string CreateFailed = "OpenApp:CreateFailed";
    public const string InvalidUri = "OpenApp:InvalidUri";
    public const string MachineScopeRejectsUserGrants = "OpenApp:MachineScopeRejectsUserGrants";
    public const string MachineScopeRequiresClientCredentials = "OpenApp:MachineScopeRequiresClientCredentials";
    public const string MachineScopeRequiresConfidential = "OpenApp:MachineScopeRequiresConfidential";
    public const string MachineScopeRequiresTokenEndpoint = "OpenApp:MachineScopeRequiresTokenEndpoint";
    public const string NotFound = "OpenApp:NotFound";
    public const string PkceRequired = "OpenApp:PkceRequired";
    public const string ScopeUnsupported = "OpenApp:ScopeUnsupported";
    public const string SecretResetConfidentialOnly = "OpenApp:SecretResetConfidentialOnly";
}
