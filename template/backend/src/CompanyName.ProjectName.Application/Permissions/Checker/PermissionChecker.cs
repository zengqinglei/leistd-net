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
    IRepository<UserRole, Guid> userRoleRepository) : IPermissionChecker
{
    public async Task<bool> IsGrantedAsync(string name, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        var subject = await GetCurrentSubjectAsync(cancellationToken);
        if (subject == null)
            return false;

        if (subject.IsSuperAdmin)
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

        if (subject.IsSuperAdmin)
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
            return new PermissionSubject(userIdValue.ToString(), [], IsSuperAdmin: true);
        }

        var roleIds = (await userRoleRepository.GetListAsync(ur => ur.UserId == userIdValue, cancellationToken))
            .Select(ur => ur.RoleId.ToString())
            .ToArray();

        return new PermissionSubject(
            userIdValue.ToString(),
            roleIds,
            IsSuperAdmin: false);
    }

    private sealed record PermissionSubject(
        string UserId,
        IReadOnlyCollection<string> RoleIds,
        bool IsSuperAdmin);
}
