using Leistd.ExceptionHandling;

namespace Leistd.MultiTenancy.Exceptions;

/// <summary>
/// 表示租户名称已被占用。
/// </summary>
/// <param name="normalizedName">冲突的归一化名称</param>
public class DuplicateTenantNameException(string normalizedName)
    : ConflictException($"Tenant name already exists: {normalizedName}")
{
    /// <summary>
    /// 获取冲突的归一化名称。
    /// </summary>
    public string NormalizedName { get; } = normalizedName;
}
