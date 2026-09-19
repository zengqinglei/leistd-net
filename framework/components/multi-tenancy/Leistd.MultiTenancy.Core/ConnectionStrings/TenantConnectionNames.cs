using System.Text.RegularExpressions;
using Leistd.Data.Constants;

namespace Leistd.MultiTenancy.ConnectionStrings;

// 连接名的归一化与校验。名字来自两处：使用方 DbContext 的 [ConnectionStringName]，以及管理员在租户管理里填的值。
// 统一小写后存储与查询，"Crm" 与 "crm" 因此命中同一行——大小写差异本来会表现成"登记过却解析不到"，
// 那是最难排查的一类支持问题。
internal static class TenantConnectionNames
{
    // 归一化之后才匹配，所以模式里只有小写
    private static readonly Regex Pattern =
        new(TenantConnectionConfiguration.NamePattern, RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>默认连接名归一化后的值。</summary>
    public static readonly string Default = Normalize(ConnectionStringNames.Default);

    public static string Normalize(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var normalized = name.Trim().ToLowerInvariant();
        if (!Pattern.IsMatch(normalized))
        {
            throw new ArgumentException(
                $"Connection name '{name}' is invalid. After lowercasing it must match " +
                $"{TenantConnectionConfiguration.NamePattern}.",
                nameof(name));
        }

        return normalized;
    }
}
