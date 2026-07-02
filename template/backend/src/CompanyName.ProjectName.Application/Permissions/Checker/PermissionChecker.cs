using CompanyName.ProjectName.Domain.Users.Constants;
using CompanyName.ProjectName.Domain.Users.Entities;
using Leistd.Authorization;
using Leistd.Ddd.Domain.Repositories;
using Leistd.Security.Users;

namespace CompanyName.ProjectName.Application.Permissions.Checker;

/// <summary>
/// 权限检查器实现
/// </summary>
public class PermissionChecker(
    ICurrentUser currentUser,
    IPermissionGrantStore permissionGrantStore,
    IRepository<User, Guid> userRepository,
    IRepository<UserRole, Guid> userRoleRepository,
    IRepository<Role, Guid> roleRepository) : IPermissionChecker
{
    public async Task<bool> IsGrantedAsync(string name, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        var subject = await GetCurrentSubjectAsync(cancellationToken);
        if (subject == null)
            return false;

        if (subject.HasAllPermissions)
            return true;

        var results = await permissionGrantStore.IsGrantedToUserOrRolesAsync(
            [name],
            subject.UserId,
            subject.RoleIds,
            cancellationToken);

        return results.TryGetValue(name, out var isGranted) && isGranted;
    }

    public async Task<MultiplePermissionGrantResult> IsGrantedAsync(
        string[] names,
        CancellationToken cancellationToken = default)
    {
        if (names == null || names.Length == 0)
            return new MultiplePermissionGrantResult(new Dictionary<string, bool>());

        var results = names
            .Distinct(StringComparer.Ordinal)
            .ToDictionary(x => x, _ => false, StringComparer.Ordinal);

        var permissionNames = results.Keys
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToArray();

        if (permissionNames.Length == 0)
            return new MultiplePermissionGrantResult(results);

        var subject = await GetCurrentSubjectAsync(cancellationToken);
        if (subject == null)
            return new MultiplePermissionGrantResult(results);

        if (subject.HasAllPermissions)
        {
            foreach (var name in permissionNames)
            {
                results[name] = true;
            }

            return new MultiplePermissionGrantResult(results);
        }

        var grants = await permissionGrantStore.IsGrantedToUserOrRolesAsync(
            permissionNames,
            subject.UserId,
            subject.RoleIds,
            cancellationToken);

        foreach (var (name, isGranted) in grants)
        {
            results[name] = isGranted;
        }

        return new MultiplePermissionGrantResult(results);
    }

    private async Task<PermissionSubject?> GetCurrentSubjectAsync(CancellationToken cancellationToken)
    {
        var userId = currentUser.Id;
        if (!userId.HasValue)
            return null;

        var userIdValue = userId.Value;
        var user = await userRepository.GetByIdAsync(userIdValue, cancellationToken);
        if (user?.IsSuperAdmin == true)
        {
            return new PermissionSubject(userIdValue.ToString(), [], HasAllPermissions: true);
        }

        var userRoles = (await userRoleRepository.GetListAsync(ur => ur.UserId == userIdValue, cancellationToken)).ToList();
        var roleIds = userRoles.Select(ur => ur.RoleId).ToList();
        var roleNames = currentUser.GetRoles().ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (roleIds.Count > 0)
        {
            var dbRoles = await roleRepository.GetListAsync(r => roleIds.Contains(r.Id), cancellationToken);
            foreach (var roleName in dbRoles.Select(role => role.Name))
            {
                roleNames.Add(roleName);
            }
        }

        return new PermissionSubject(
            userIdValue.ToString(),
            roleIds.Select(x => x.ToString()).ToArray(),
            roleNames.Contains(AdminConstant.RoleName));
    }

    private sealed record PermissionSubject(
        string UserId,
        IReadOnlyCollection<string> RoleIds,
        bool HasAllPermissions);
}
