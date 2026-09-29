using Leistd.ExceptionHandling;
using Leistd.MultiTenancy.Errors;

namespace Leistd.MultiTenancy.Exceptions;

/// <summary>
/// 表示已认证主体的租户声明非法：多于一条，或值不是租户 GUID。
/// </summary>
/// <remarks>
/// <para>一律失败关闭，不选取第一条，也不回落到宿主上下文——回落等于让租户主体看到宿主数据。</para>
/// <para>租户可能正常存在，出错的是凭据形状；应在签发侧或身份来源修正，重试同一凭据无效。</para>
/// </remarks>
public class InvalidTenantClaimException : BusinessException
{
    /// <summary>构造异常。</summary>
    /// <param name="claimType">租户 claim 类型</param>
    public InvalidTenantClaimException(string claimType)
        : base(MultiTenancyErrorCodes.InvalidTenantClaim,
            "The authenticated identity carries invalid tenant information. Sign in again.")
    {
        ClaimType = claimType;
    }

    /// <summary>获取租户声明类型。</summary>
    public string ClaimType { get; }
}
