using Leistd.Ddd.Domain.Entities.Auditing;
#if (IncludeTenancy)
using Leistd.MultiTenancy;
#endif

namespace CompanyName.ProjectName.Domain.Users.Entities;

#if (IncludeTenancy)
public class User : FullAuditedEntity<Guid>, IMultiTenant
#else
public class User : FullAuditedEntity<Guid>
#endif
{
#if (IncludeTenancy)
    /// <summary>
    /// 所属租户（null 为宿主用户），由多租户落值拦截器在创建时填充
    /// </summary>
    public Guid? TenantId { get; private set; }

#endif
    /// <summary>
    /// 用户名（租户内唯一）
    /// </summary>
    public string Username { get; private set; }

    /// <summary>
    /// 邮箱（租户内唯一）
    /// </summary>
    public string Email { get; private set; }

#if (IncludeIdentity)
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

#if (IncludeIdentity)
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

    public User(string username, string email, string? passwordHash = null, string? displayName = null)
    {
        Id = Guid.CreateVersion7();
        Username = username;
        Email = email;
#if (IncludeIdentity)
        PasswordHash = passwordHash;
#endif
        DisplayName = displayName ?? username;
    }

    public void Update(string? displayName, string? phoneNumber, string? avatar)
    {
        DisplayName = displayName;
#if (IncludeIdentity)
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
#if (IncludeIdentity)
        EmailConfirmed = emailConfirmed;
#endif
    }

    public void UpdateProfile(string username, string email, string? displayName, string? phoneNumber, string? avatar)
    {
        Username = username;
        Email = email;
        DisplayName = displayName;
#if (IncludeIdentity)
        PhoneNumber = phoneNumber;
#endif
        Avatar = avatar;
    }

    public void MarkAsSuperAdmin()
    {
        IsSuperAdmin = true;
    }

    public void Enable()
    {
        IsActive = true;
    }

    public void Disable()
    {
        IsActive = false;
    }

#if (IncludeIdentity)
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

    public void RecordLoginSuccess(string? ip = null)
    {
        LastLoginTime = DateTime.UtcNow;
        LastLoginIp = ip;
        AccessFailedCount = 0;
    }

    public bool IsLockedOut()
    {
        return IsLocked && (!LockoutEnd.HasValue || LockoutEnd.Value > DateTime.UtcNow);
    }
#endif
}
