using Leistd.Ddd.Domain.Entities.Auditing;
using Leistd.MultiTenancy;
using CompanyName.ProjectName.Domain.Users.ValueObjects;
using Leistd.MultiTenancy.Abstractions;

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
#endif

    private User()
    {
        Username = null!;
        Email = null!;
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

    public void UpdateProfile(string username, string email, string? displayName, string? phoneNumber, string? avatar)
    {
        Username = username;
        Email = email;
        DisplayName = displayName;
#if (LocalIdentity)
        PhoneNumber = phoneNumber;
#endif
        Avatar = avatar;
    }

    public void MarkAsSuperAdmin()
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
    }

    public void ConfirmEmail()
    {
        EmailConfirmed = true;
    }

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

    public void RecordAccessFailed()
    {
        AccessFailedCount++;
    }

    public void RecordLoginSuccess(DateTime now, string? ip = null)
    {
        LastLoginTime = now;
        LastLoginIp = ip;
        AccessFailedCount = 0;
    }

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
