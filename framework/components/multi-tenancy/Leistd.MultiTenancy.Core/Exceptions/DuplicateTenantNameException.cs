using Leistd.Exception.Core;

namespace Leistd.MultiTenancy;

/// <summary>
/// 租户名称冲突异常。继承 <see cref="ConflictException"/>，经全局异常处理器映射为 HTTP 409
/// </summary>
/// <param name="normalizedName">冲突的归一化名称</param>
public class DuplicateTenantNameException(string normalizedName)
    : ConflictException($"Tenant name already exists: {normalizedName}")
{
    /// <summary>
    /// 冲突的归一化名称
    /// </summary>
    public string NormalizedName { get; } = normalizedName;
}
