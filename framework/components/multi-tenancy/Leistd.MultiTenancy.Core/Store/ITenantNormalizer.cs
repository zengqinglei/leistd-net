namespace Leistd.MultiTenancy;

/// <summary>
/// 租户名称归一化器：使名称查询大小写不敏感
/// </summary>
public interface ITenantNormalizer
{
    /// <summary>
    /// 归一化租户名称（null 原样返回）
    /// </summary>
    string? NormalizeName(string? name);
}

/// <summary>
/// 默认实现：Unicode 规范化后转大写（Invariant）
/// </summary>
public class UpperInvariantTenantNormalizer : ITenantNormalizer
{
    /// <inheritdoc />
    public string? NormalizeName(string? name) => name?.Normalize().ToUpperInvariant();
}
