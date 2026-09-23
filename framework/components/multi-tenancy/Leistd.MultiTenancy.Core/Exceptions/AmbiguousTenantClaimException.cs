using Leistd.ExceptionHandling;
using Leistd.MultiTenancy.ConnectionStrings;
using Leistd.MultiTenancy.Context;
using Leistd.MultiTenancy.Errors;
using Leistd.MultiTenancy.Management;
using Leistd.MultiTenancy.Tenancy;

namespace Leistd.MultiTenancy.Exceptions;

/// <summary>
/// 表示已认证主体包含多条租户声明。
/// </summary>
/// <remarks>
/// <para>多条 claim 一律失败关闭，不选取第一条，也不回落到宿主上下文。</para>
/// <para>租户可能正常存在，出错的是凭据形状；应在签发侧或身份来源修正，重试同一凭据无效。</para>
/// </remarks>
public class AmbiguousTenantClaimException : BusinessException
{
    /// <summary>构造异常。</summary>
    /// <param name="claimType">租户 claim 类型</param>
    /// <param name="count">实际出现的条数</param>
    public AmbiguousTenantClaimException(string claimType, int count)
        : base(MultiTenancyErrorCodes.AmbiguousTenantClaim,
            "The authenticated identity contains conflicting tenant information. Sign in again.")
    {
        ClaimType = claimType;
        Count = count;
    }

    /// <summary>获取租户声明类型。</summary>
    public string ClaimType { get; }

    /// <summary>获取租户声明数量。</summary>
    public int Count { get; }
}
