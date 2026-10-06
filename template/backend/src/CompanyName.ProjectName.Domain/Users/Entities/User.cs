#if (LocalIdentity)
using CompanyName.ProjectName.Domain.Auth.Errors;
using Leistd.ExceptionHandling;
#endif
using Leistd.Ddd.Domain.Entities.Auditing;
using Leistd.MultiTenancy;
using CompanyName.ProjectName.Domain.Users.ValueObjects;
using CompanyName.ProjectName.Domain.Users.DomainServices;
using CompanyName.ProjectName.Domain.Users.Policies;
using Leistd.MultiTenancy.ConnectionStrings;
using Leistd.MultiTenancy.Context;
using Leistd.MultiTenancy.Errors;
using Leistd.MultiTenancy.Tenancy;

namespace CompanyName.ProjectName.Domain.Users.Entities;

public class User : FullAuditedEntity<Guid>, IMultiTenant
{
    /// <summary>
    /// 所属租户（null 为宿主用户），由多租户落值拦截器在创建时填充
    /// </summary>
    public Guid? TenantId { get; private set; }

    /// <summary>
    /// 用户名（租户内唯一）
    /// </summary>
    public string Username { get; private set; }

    /// <summary>
    /// 邮箱（租户内唯一）
    /// </summary>
    public string Email { get; private set; }

#if (LocalIdentity)
    /// <summary>
    /// 邮箱是否已验证
    /// </summary>
    public bool EmailConfirmed { get; private set; }

    /// <summary>
    /// 密码哈希（可为空，OAuth 用户无密码）
    /// </summary>
    public string? PasswordHash { get; private set; }

    /// <summary>
    /// 手机号
    /// </summary>
    public string? PhoneNumber { get; private set; }

    /// <summary>
    /// 手机号是否已验证
    /// </summary>
    public bool PhoneNumberConfirmed { get; private set; }
#endif

    /// <summary>
    /// 头像
    /// </summary>
    public string? Avatar { get; private set; }

    /// <summary>
    /// 显示名称
    /// </summary>
    public string? DisplayName { get; private set; }

    /// <summary>
    /// 是否启用
    /// </summary>
    public bool IsActive { get; private set; } = true;

    /// <summary>
    /// 是否系统内置超级管理员
    /// </summary>
    public bool IsSuperAdmin { get; private set; }

#if (LocalIdentity)
    /// <summary>
    /// 是否锁定
    /// </summary>
    public bool IsLocked { get; private set; }

    /// <summary>
    /// 锁定截止时间
    /// </summary>
    public DateTime? LockoutEnd { get; private set; }

    /// <summary>
    /// 访问失败次数
    /// </summary>
    public int AccessFailedCount { get; private set; }

    /// <summary>
    /// 最后登录时间
    /// </summary>
    public DateTime? LastLoginTime { get; private set; }

    /// <summary>
    /// 最后登录 IP
    /// </summary>
    public string? LastLoginIp { get; private set; }

    /// <summary>
    /// 是否已启用两步验证
    /// </summary>
    public bool TwoFactorEnabled { get; private set; }

    /// <summary>
    /// 两步验证密钥（经 <c>TwoFactorDomainService</c> 用 Data Protection 加密）
    /// </summary>
    public string? TwoFactorSecret { get; private set; }

    /// <summary>
    /// 尚未使用的恢复码摘要，以 <c>;</c> 分隔
    /// </summary>
    /// <remarks>一个用户至多十个、用过即删，不值得为它单开一张表。</remarks>
    public string? TwoFactorRecoveryCodes { get; private set; }

    /// <summary>
    /// 最近一次校验通过的验证码所在步序号；不大于它的步一律拒绝，防止同一个码被重放
    /// </summary>
    public long? TwoFactorLastUsedStep { get; private set; }

    /// <summary>
    /// 账号安全版本：凭据（口令、两步验证、外部登录绑定）每变一次就换一个新值
    /// </summary>
    /// <remarks>
    /// 登录第二步的挑战记下签发时的值，完成前比对：第一步通过之后改了口令、重置或解绑了凭据，
    /// 旧挑战随之作废。挑战不是会话，撤销会话挡不住它。
    /// </remarks>
    public string SecurityStamp { get; private set; }
#endif

    private User()
    {
        Username = null!;
        Email = null!;
#if (LocalIdentity)
        SecurityStamp = null!;
#endif
    }

    public User(
#if (!LocalIdentity)
        Guid subjectId,
#endif
        string username,
        string email,
        string? passwordHash = null,
        string? displayName = null)
    {
#if (LocalIdentity)
        Id = Guid.CreateVersion7();
#else
        if (subjectId == Guid.Empty) throw new ArgumentException("Subject Id cannot be empty.", nameof(subjectId));
        Id = subjectId;
#endif
        Username = username;
        Email = email;
#if (LocalIdentity)
        PasswordHash = passwordHash;
        SecurityStamp = NewSecurityStamp();
#endif
        DisplayName = displayName ?? username;
    }

    public void Update(string? displayName, string? phoneNumber, string? avatar)
    {
        DisplayName = displayName;
#if (LocalIdentity)
        PhoneNumber = phoneNumber;
#endif
        Avatar = avatar;
    }

#if (!LocalIdentity)
    /// <summary>按签发方令牌刷新资料字段。</summary>
    /// <remarks>
    /// 资源服务形态下这三个字段归签发方所有，本地没有编辑入口，只有这一条刷新通道。
    /// <b>刻意不碰 <see cref="IsActive"/> 与角色</b>：那是本服务自己的授权决定，
    /// 跟着刷新走会把管理员刚做的启停与授权冲掉，而且每个请求冲一次。
    /// </remarks>
    public void ProjectFromIssuer(string username, string email, string? displayName)
    {
        Username = username;
        Email = email;
        DisplayName = displayName;
    }

#endif
    public void UpdateManagement(string email, string? displayName, string? avatar, bool isActive, bool emailConfirmed)
    {
        Email = email;
        DisplayName = displayName;
        Avatar = avatar;
        IsActive = isActive;
#if (LocalIdentity)
        EmailConfirmed = emailConfirmed;
#endif
    }

    /// <summary>
    /// 本人修改资料。头像另有入口（<see cref="SetAvatar"/>），不随资料表单一起提交。
    /// </summary>
    /// <remarks>换了邮箱即回到"未验证"：验证过的是旧地址，新地址是否属于本人还不知道。</remarks>
    public void UpdateProfile(string username, string email, string? displayName, string? phoneNumber)
    {
#if (LocalIdentity)
        if (!string.Equals(Email, email, StringComparison.OrdinalIgnoreCase))
        {
            EmailConfirmed = false;
        }

#endif
        Username = username;
        Email = email;
        DisplayName = displayName;
#if (LocalIdentity)
        PhoneNumber = phoneNumber;
#endif
    }

    /// <summary>设置或清除头像；取值须先经 <c>AvatarPolicy.EnsureValid</c> 校验。</summary>
    public void SetAvatar(string? avatar)
    {
        Avatar = string.IsNullOrEmpty(avatar) ? null : avatar;
    }

    /// <summary>标记为宿主超级管理员。</summary>
    /// <remarks>只由 <see cref="UserDomainService"/> 调用：它在标记之前挡住租户上下文，超管必须只在宿主。</remarks>
    internal void MarkAsSuperAdmin()
    {
        IsSuperAdmin = true;
    }

    public bool CanBeManagedBy(Guid? actorUserId)
    {
        return !IsSuperAdmin || Id == actorUserId;
    }

    public bool CanBeDisabled()
    {
        return !IsSuperAdmin;
    }

    public bool CanBeDeleted()
    {
        return !IsSuperAdmin;
    }

    public void Enable()
    {
        IsActive = true;
    }

    public void Disable()
    {
        IsActive = false;
    }

#if (LocalIdentity)
    public void UpdatePasswordHash(string passwordHash)
    {
        PasswordHash = passwordHash;
        RotateSecurityStamp();
    }

    /// <summary>换一个新的安全版本，使此前签发、尚未完成的登录挑战作废。</summary>
    /// <remarks>改口令、启用或停用两步验证时自动调用；其余凭据变化（解绑外部登录）由调用方显式调用。</remarks>
    public void RotateSecurityStamp()
    {
        SecurityStamp = NewSecurityStamp();
    }

    private static string NewSecurityStamp() => Guid.NewGuid().ToString("N");

    public void ConfirmEmail()
    {
        EmailConfirmed = true;
    }
#if (Email)

    /// <summary>邮箱尚未验证；已验证时以 <see cref="AuthErrorCodes.EmailAlreadyVerified"/> 拒绝再发验证码。</summary>
    public void EnsureEmailUnconfirmed()
    {
        if (EmailConfirmed)
        {
            throw new BusinessException(AuthErrorCodes.EmailAlreadyVerified, "This email address has already been verified.");
        }
    }
#endif

    public void ConfirmPhoneNumber()
    {
        PhoneNumberConfirmed = true;
    }

    public void Lock(DateTime? lockoutEnd = null)
    {
        IsLocked = true;
        LockoutEnd = lockoutEnd;
    }

    public void Unlock()
    {
        IsLocked = false;
        LockoutEnd = null;
        AccessFailedCount = 0;
    }

    /// <summary>
    /// 记一次密码错误；累计达到 <paramref name="policy"/> 的阈值时锁定一段时间，返回 true。
    /// </summary>
    /// <remarks>
    /// 锁定时计数清零：锁定到期后重新给满一轮尝试次数。上一轮失败锁定已到期的，先解除再计数，
    /// 否则过期锁定留下的 <c>IsLocked</c> 会让界面一直显示"已锁定"。
    /// 管理员锁定（无截止时间）不经这里解除。
    /// </remarks>
    public bool RecordAccessFailed(DateTime now, LoginLockoutPolicy policy)
    {
        if (IsLocked && LockoutEnd is { } end && end <= now)
        {
            Unlock();
        }

        AccessFailedCount++;
        if (!policy.IsEnabled || AccessFailedCount < policy.MaxFailedAttempts)
        {
            return false;
        }

        Lock(now + policy.Duration);
        AccessFailedCount = 0;
        return true;
    }

    /// <summary>
    /// 处于因登录失败而起的临时锁定中（有截止时间且未到）。
    /// </summary>
    /// <remarks>
    /// 与管理员锁定（无截止时间）分开判：临时锁定可以由别人反复输错触发，
    /// 它只挡新的登录，不能拿来把已经登录的本人踢下线。
    /// </remarks>
    public bool IsTemporarilyLockedOut(DateTime now) => IsLocked && LockoutEnd is { } end && end > now;

    /// <summary>
    /// 已建立的会话与已签发的令牌还能否继续使用：账号被禁用、或被锁定且没有截止时间时不能。
    /// </summary>
    /// <remarks>
    /// 登录失败触发的临时锁定只挡新的登录：它可以由别人反复输错触发，若也作用于已在线的会话，
    /// 知道用户名就能把本人踢下线。
    /// </remarks>
    public bool AllowsExistingSessions(DateTime now) => GetAccessStatus(now) switch
    {
        UserAccessStatus.Allowed => true,
        UserAccessStatus.LockedOut => IsTemporarilyLockedOut(now),
        _ => false,
    };

    public void RecordLoginSuccess(DateTime now, string? ip = null)
    {
        LastLoginTime = now;
        LastLoginIp = ip;
        AccessFailedCount = 0;
    }

    /// <summary>剩余可用的恢复码个数。</summary>
    public int RecoveryCodesLeft => SplitRecoveryCodes().Count;

    /// <summary>
    /// 启用两步验证。
    /// </summary>
    /// <param name="protectedSecret">已加密的密钥。</param>
    /// <param name="recoveryCodeHashes">恢复码摘要。</param>
    /// <param name="usedStep">启用时校验通过的那一步，随即记为已用：同一个码不能紧接着再拿去登录。</param>
    /// <exception cref="BusinessException">两步验证已启用（<see cref="AuthErrorCodes.TwoFactorAlreadyEnabled"/>）：重新启用会静默换掉正在使用的密钥。</exception>
    public void EnableTwoFactor(string protectedSecret, IEnumerable<string> recoveryCodeHashes, long usedStep)
    {
        EnsureTwoFactorDisabled();
        TwoFactorEnabled = true;
        TwoFactorSecret = protectedSecret;
        TwoFactorRecoveryCodes = string.Join(';', recoveryCodeHashes);
        TwoFactorLastUsedStep = usedStep;
        RotateSecurityStamp();
    }

    /// <summary>停用两步验证，并清掉密钥与恢复码。</summary>
    public void DisableTwoFactor()
    {
        TwoFactorEnabled = false;
        TwoFactorSecret = null;
        TwoFactorRecoveryCodes = null;
        TwoFactorLastUsedStep = null;
        RotateSecurityStamp();
    }

    /// <summary>换一组恢复码，旧的全部作废。</summary>
    /// <exception cref="BusinessException">两步验证未启用（<see cref="AuthErrorCodes.TwoFactorNotEnabled"/>）。</exception>
    public void ReplaceRecoveryCodes(IEnumerable<string> recoveryCodeHashes)
    {
        EnsureTwoFactorEnabled();
        TwoFactorRecoveryCodes = string.Join(';', recoveryCodeHashes);
    }

    /// <summary>两步验证尚未启用；已启用时以 <see cref="AuthErrorCodes.TwoFactorAlreadyEnabled"/> 拒绝。</summary>
    public void EnsureTwoFactorDisabled()
    {
        if (TwoFactorEnabled)
        {
            throw new BusinessException(AuthErrorCodes.TwoFactorAlreadyEnabled, "Two-factor authentication is already turned on.");
        }
    }

    /// <summary>两步验证已启用；未启用时以 <see cref="AuthErrorCodes.TwoFactorNotEnabled"/> 拒绝。</summary>
    public void EnsureTwoFactorEnabled()
    {
        if (!TwoFactorEnabled)
        {
            throw new BusinessException(AuthErrorCodes.TwoFactorNotEnabled, "Two-factor authentication is not turned on.");
        }
    }

    /// <summary>记下校验通过的步序号。</summary>
    public void RecordTwoFactorStep(long step)
    {
        TwoFactorLastUsedStep = step;
    }

    /// <summary>用掉一个恢复码；摘要不在剩余列表里时返回 false。</summary>
    public bool TryConsumeRecoveryCode(string hash)
    {
        var codes = SplitRecoveryCodes();
        if (!codes.Remove(hash))
            return false;

        TwoFactorRecoveryCodes = codes.Count == 0 ? null : string.Join(';', codes);
        return true;
    }

    private List<string> SplitRecoveryCodes() =>
        [.. (TwoFactorRecoveryCodes ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries)];

#endif

    public UserAccessStatus GetAccessStatus(DateTime now)
    {
        if (!IsActive)
        {
            return UserAccessStatus.Disabled;
        }

#if (LocalIdentity)
        if (IsLocked && (!LockoutEnd.HasValue || LockoutEnd.Value > now))
        {
            return UserAccessStatus.LockedOut;
        }
#endif

        return UserAccessStatus.Allowed;
    }
}
