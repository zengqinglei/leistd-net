namespace Leistd.Authorization;

/// <summary>
/// 默认权限检查器。
/// </summary>
public class DefaultPermissionChecker(
    IPermissionSubjectProvider subjectProvider,
    IPermissionGrantStore permissionGrantStore) : IPermissionChecker
{
    public async Task<bool> IsGrantedAsync(string name, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        var subject = await subjectProvider.GetCurrentSubjectAsync(cancellationToken);
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

        var subject = await subjectProvider.GetCurrentSubjectAsync(cancellationToken);
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
}
