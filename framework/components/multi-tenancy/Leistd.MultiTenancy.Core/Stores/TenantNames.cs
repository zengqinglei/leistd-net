using System.Text.RegularExpressions;
using Leistd.ExceptionHandling;
using Leistd.MultiTenancy.Errors;

namespace Leistd.MultiTenancy.Stores;

// 租户名的写入前校验，创建与改名共用。名字来自管理员输入，不合法是调用方能改对的错误：400 而不是 500。
// 只校验不改写：落库的仍是调用方给的原值，大小写归一由 ITenantNormalizer 负责。
internal static class TenantNames
{
    private static readonly Regex Pattern =
        new(TenantConfiguration.NamePattern, RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static void EnsureValid(string? name)
    {
        // 比对匹配长度：.NET 的 $ 也匹配末尾换行之前，"acme\n" 否则会被放行
        if (name is not null && Pattern.Match(name) is { Success: true } match && match.Length == name.Length)
        {
            return;
        }

        throw new BusinessException(MultiTenancyErrorCodes.NameInvalid,
                $"Tenant name '{name}' is invalid. It must match {TenantConfiguration.NamePattern}.")
            .WithData("Name", name)
            .WithData("Pattern", TenantConfiguration.NamePattern);
    }
}
