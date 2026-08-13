using System.Security.Claims;
using Leistd.Security.Users;

namespace Leistd.TestBase;

/// <summary>
/// 可控当前用户：测试中设定 Id / 角色 / Claims，替代真实 <c>ICurrentUser</c> 实现。
/// 角色匹配不区分大小写，与框架约定一致。
/// </summary>
public sealed class FakeCurrentUser : ICurrentUser
{
    private readonly string[] _roles;
    private readonly Claim[] _claims;

    public FakeCurrentUser(
        Guid? id = null,
        string? username = null,
        string? name = null,
        string? email = null,
        string? phoneNumber = null,
        string[]? roles = null,
        Claim[]? claims = null,
        Guid? tenantId = null)
    {
        Id = id;
        Username = username;
        Name = name;
        Email = email;
        PhoneNumber = phoneNumber;
        TenantId = tenantId;
        _roles = roles ?? [];
        _claims = claims ?? [];
    }

    public bool IsAuthenticated => Id.HasValue;
    public Guid? Id { get; }
    public Guid? TenantId { get; }
    public string? Username { get; }
    public string? Name { get; }
    public string? Email { get; }
    public string? PhoneNumber { get; }

    public string[] GetRoles() => _roles;

    public bool IsInRole(string roleName) =>
        _roles.Any(r => string.Equals(r, roleName, StringComparison.OrdinalIgnoreCase));

    public Claim? FindClaim(string claimType) =>
        _claims.FirstOrDefault(c => c.Type == claimType);

    public Claim[] FindClaims(string claimType) =>
        _claims.Where(c => c.Type == claimType).ToArray();

    public Claim[] GetAllClaims() => _claims;
}
