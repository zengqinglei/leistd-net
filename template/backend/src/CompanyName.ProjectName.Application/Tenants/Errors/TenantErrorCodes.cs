namespace CompanyName.ProjectName.Application.Tenants.Errors;

/// <summary>Tenant 业务错误码。</summary>
public static class TenantErrorCodes
{
    public const string ActivateWithoutUsers = "Tenant:ActivateWithoutUsers";
    public const string AdministratorNotFound = "Tenant:AdministratorNotFound";
    public const string AlreadyImpersonating = "Tenant:AlreadyImpersonating";
    public const string ImpersonationRequiresAuthentication = "Tenant:ImpersonationRequiresAuthentication";
    public const string NotImpersonating = "Tenant:NotImpersonating";
}
