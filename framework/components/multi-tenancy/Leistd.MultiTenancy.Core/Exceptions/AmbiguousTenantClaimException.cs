using Leistd.ExceptionHandling;

namespace Leistd.MultiTenancy.Exceptions;

/// <summary>
/// 表示已认证主体包含多条租户声明。
/// </summary>
/// <remarks>
/// 多条 claim 一律失败关闭，不选取第一条，也不回落到宿主上下文。
/// </remarks>
/// <param name="claimType">租户 claim 类型</param>
/// <param name="count">实际出现的条数</param>
public class AmbiguousTenantClaimException(string claimType, int count)
    : NotFoundException(
        $"The authenticated principal carries {count} '{claimType}' claims; exactly one is required. " +
        "Duplicate tenant claims are rejected even when their values are identical.")
{
    /// <summary>获取租户声明类型。</summary>
    public string ClaimType { get; } = claimType;

    /// <summary>获取租户声明数量。</summary>
    public int Count { get; } = count;
}
