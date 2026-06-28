using Microsoft.AspNetCore.Authorization;

namespace CompanyName.ProjectName.Api.Authorization;

/// <summary>
/// 仅超级管理员可访问。基于 "SuperAdmin" 授权策略（is_super_admin claim）。
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class SuperAdminOnlyAttribute() : AuthorizeAttribute("SuperAdmin");
