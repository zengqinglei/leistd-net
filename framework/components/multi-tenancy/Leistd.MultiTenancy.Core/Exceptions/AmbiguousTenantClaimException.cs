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
/// <para><b>这是 400 而不是 404</b>：租户可能好端端地存在，出问题的是这一次请求携带的凭据形状。
/// 报成"找不到租户"会把排查引向租户注册表，而真正的原因在签发侧或身份来源被拼接。
/// 也不是 401：拿同一个畸形令牌重新认证只会原地打转，这个请求重试无用。</para>
/// </remarks>
public class AmbiguousTenantClaimException : BadRequestException
{
    /// <summary>构造异常。</summary>
    /// <param name="claimType">租户 claim 类型</param>
    /// <param name="count">实际出现的条数</param>
    public AmbiguousTenantClaimException(string claimType, int count)
        : base(
            $"The authenticated principal carries {count} '{claimType}' claims; exactly one is required. " +
            "Duplicate tenant claims are rejected even when their values are identical.")
    {
        ClaimType = claimType;
        Count = count;
        WithCode(MultiTenancyErrorCodes.AmbiguousTenantClaim);
    }

    /// <summary>获取租户声明类型。</summary>
    public string ClaimType { get; }

    /// <summary>获取租户声明数量。</summary>
    public int Count { get; }
}
