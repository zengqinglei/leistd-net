#if (LocalIdentity)
using CompanyName.ProjectName.Domain.Users.Entities;

namespace CompanyName.ProjectName.Domain.Users.ValueObjects;

/// <summary>校验用户名密码的结果。</summary>
public enum CredentialValidationStatus
{
    /// <summary>用户名密码正确。账号是否可登录（停用等）由签发会话时另行判定。</summary>
    Succeeded,

    /// <summary>用户不存在或密码错误——两者对外不作区分。</summary>
    InvalidCredentials,

    /// <summary>账号处于锁定中，未校验密码。</summary>
    LockedOut
}

/// <summary>
/// 校验用户名密码的结果。
/// </summary>
/// <param name="Status">结果。</param>
/// <param name="User">匹配到的用户；用户不存在时为 null。</param>
/// <param name="Countable">
/// 这次失败该不该计入失败次数。只有"账号存在、有本地口令、不在锁定中、口令不对"才为真：
/// 没有本地口令的账号没有可猜的口令，计数只会让别人能把它锁住、连外部登录一起挡在外面。
/// 累计动作本身由应用层执行（见 <c>IAccessFailureCounter</c>）。
/// </param>
public sealed record CredentialValidationResult(
    CredentialValidationStatus Status,
    User? User,
    bool Countable = false);
#endif
