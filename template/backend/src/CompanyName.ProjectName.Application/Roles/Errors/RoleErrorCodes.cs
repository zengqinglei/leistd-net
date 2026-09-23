namespace CompanyName.ProjectName.Application.Roles.Errors;

/// <summary>Role 业务错误码。</summary>
public static class RoleErrorCodes
{
    public const string NameAlreadyUsed = "Role:NameAlreadyUsed";
    public const string NameTooLong = "Role:NameTooLong";
    public const string NotFound = "Role:NotFound";
    public const string RoleStillAssigned = "Role:RoleStillAssigned";
    public const string StaticRoleCannotBeDeleted = "Role:StaticRoleCannotBeDeleted";
}
