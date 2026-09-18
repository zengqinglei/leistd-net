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
/// <param name="LockoutTriggered">本次失败恰好触发了锁定（调用方据此留一条记录）。</param>
public sealed record CredentialValidationResult(
    CredentialValidationStatus Status,
    User? User,
    bool LockoutTriggered = false);
#endif
