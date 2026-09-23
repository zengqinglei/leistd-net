#if (LocalIdentity)
using CompanyName.ProjectName.Domain.Users.Policies;
using CompanyName.ProjectName.Domain.Users.ValueObjects;
#endif
using CompanyName.ProjectName.Domain.Shared.Security.Errors;
using CompanyName.ProjectName.Domain.Users.Errors;
using CompanyName.ProjectName.Domain.Shared.Security.PasswordHash;
using CompanyName.ProjectName.Domain.Users.Entities;
using Leistd.Ddd.Domain.Repositories;
#if (LocalIdentity)
using Leistd.MultiTenancy;
#endif
using Microsoft.Extensions.Logging;
using Leistd.ExceptionHandling;
using Leistd.MultiTenancy.ConnectionStrings;
using Leistd.MultiTenancy.Context;
using Leistd.MultiTenancy.Errors;
using Leistd.MultiTenancy.Management;
using Leistd.MultiTenancy.Tenancy;

namespace CompanyName.ProjectName.Domain.Users.DomainServices;

/// <summary>
/// 用户领域服务
/// </summary>
public class UserDomainService(
    IRepository<User, Guid> userRepository,
#if (LocalIdentity)
    // 只有 CreateSuperAdminAsync 用它挡"租户上下文里造超管"，而那个方法只在本地身份形态存在
    ICurrentTenant currentTenant,
    IRepository<Role, Guid> roleRepository,
    IRepository<UserRole, Guid> userRoleRepository,
    IPasswordHasher passwordHasher,
#endif
    ILogger<UserDomainService> logger)
{
#if (!LocalIdentity)
    /// <summary>
    /// 把签发方的主体投影成本地用户行：不存在就建，存在就按令牌刷新资料字段。
    /// </summary>
    /// <remarks>
    /// <para>本形态下用户行的主键<b>就是</b>签发方的 <c>sub</c>（见 <see cref="User"/> 的构造函数），
    /// 所以这条投影不可能"对错人"——而让人手填主体标识就可能，且抄错时不报错。</para>
    /// <para><b>资料字段每次刷新，不做本地编辑。</b>用户名、邮箱、显示名归签发方所有；
    /// 本服务没有回写通道，允许本地改只会积累与签发方的漂移。</para>
    /// <para><b>角色与启停不碰。</b>那两样是本服务自己的授权决定，刷新资料时必须原样保留，
    /// 否则每次请求都会把管理员刚做的授权冲掉。</para>
    /// <para>首次访问的并发由主键兜底：插入撞键就重读那一行——那是一次正常的竞争，不是错误。</para>
    /// </remarks>
    /// <param name="subjectId">签发方主体标识，取自令牌的 <c>sub</c>。</param>
    /// <param name="username">令牌里的用户名；缺失时回落为主体标识，保证非空且可检索。</param>
    /// <param name="email">令牌里的邮箱；缺失时留空串。</param>
    /// <param name="displayName">令牌里的展示名。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<User> EnsureProjectedAsync(
        Guid subjectId,
        string? username,
        string? email,
        string? displayName,
        CancellationToken cancellationToken = default)
    {
        var name = string.IsNullOrWhiteSpace(username) ? subjectId.ToString() : username.Trim();
        var mail = email?.Trim() ?? string.Empty;
        var display = displayName?.Trim();

        var existing = await userRepository.GetByIdAsync(subjectId, cancellationToken);
        if (existing is not null)
        {
            // 没变就不写：这条路径在每个已认证请求上都会走到
            if (existing.Username == name && existing.Email == mail && existing.DisplayName == display)
            {
                return existing;
            }

            existing.ProjectFromIssuer(name, mail, display);
            await userRepository.UpdateAsync(existing, cancellationToken);
            return existing;
        }

        var user = new User(subjectId, name, mail, displayName: display);
        try
        {
            await userRepository.InsertAsync(user, cancellationToken);
        }
        catch (Exception exception)
        {
            // 同一主体的两个首次请求撞在一起：谁先谁后都对，读回胜出的那一行即可。
            // 重读为空说明失败另有原因（约束、连接），原样抛出，不吞。
            var raced = await userRepository.GetByIdAsync(subjectId, cancellationToken);
            if (raced is null)
            {
                throw;
            }

            logger.LogDebug(exception, "Concurrent first-touch projection for subject {SubjectId}; reusing the winning row.", subjectId);
            return raced;
        }

        logger.LogInformation("Projected issuer subject {SubjectId} into a local user row.", subjectId);
        return user;
    }

#endif
    /// <summary>
    /// 检查用户名是否可用
    /// </summary>
    public async Task<bool> IsUsernameAvailableAsync(string username, CancellationToken cancellationToken = default)
    {
        return !await userRepository.AnyAsync(u => u.Username == username, cancellationToken);
    }

    /// <summary>
    /// 检查用户名是否可用（排除指定用户）
    /// </summary>
    public async Task<bool> IsUsernameAvailableAsync(Guid excludeUserId, string username, CancellationToken cancellationToken = default)
    {
        return !await userRepository.AnyAsync(u => u.Id != excludeUserId && u.Username == username, cancellationToken);
    }

    /// <summary>
    /// 检查邮箱是否可用
    /// </summary>
    public async Task<bool> IsEmailAvailableAsync(string email, CancellationToken cancellationToken = default)
    {
        return !await userRepository.AnyAsync(u => u.Email == email, cancellationToken);
    }

    /// <summary>
    /// 检查邮箱是否可用（排除指定用户）
    /// </summary>
    public async Task<bool> IsEmailAvailableAsync(Guid excludeUserId, string email, CancellationToken cancellationToken = default)
    {
        return !await userRepository.AnyAsync(u => u.Id != excludeUserId && u.Email == email, cancellationToken);
    }

#if (LocalIdentity)
    /// <summary>
    /// 校验口令是否满足服务端策略，通过后返回哈希
    /// </summary>
    /// <param name="password">明文口令</param>
    /// <param name="subject">错误消息里的主体描述</param>
    /// <remarks>
    /// <para>本领域服务是用户口令落值的唯一出口：创建、改密、重置、种子路径都经由它，
    /// 因此策略只在这里施加一次。DTO 与前端只做快速反馈，不承担安全不变量——
    /// 内部调用（种子、租户初始化、bootstrap）根本不经过 DTO。</para>
    /// <para>各入口各判各的时，系统的真实下限等于最宽的那一条。</para>
    /// </remarks>
    private string HashWithPolicy(string? password, string subject = "Password")
    {
        PasswordPolicy.Ensure(password, subject);
        return passwordHasher.HashPassword(password!);
    }

    /// <summary>
    /// 创建<b>宿主</b>引导管理员（<c>IsSuperAdmin</c>）
    /// </summary>
    /// <remarks>
    /// <c>IsSuperAdmin</c> 是宿主专用的权限管理防锁死主体，会旁路功能权限、资源授权和
    /// 数据范围，且不可停用或删除。租户管理员通过普通 Admin 角色管理权限，不得获得此标记。
    /// 在租户上下文中调用会以 <see cref="InvalidOperationException"/> 拒绝。
    /// </remarks>
    /// <exception cref="InvalidOperationException">当前存在租户上下文。</exception>
    public async Task<User> CreateSuperAdminAsync(
        string username,
        string email,
        string password,
        string? displayName,
        string passwordSubject,
        CancellationToken cancellationToken = default)
    {
        if (currentTenant.IsAvailable)
        {
            throw new InvalidOperationException(
                $"Cannot create a super admin inside tenant '{currentTenant.Id}'. IsSuperAdmin is the host " +
                "bootstrap escape hatch and must stay host-only; tenant administrators are ordinary users " +
                "holding the tenant's Admin role. Use CreateUserAsync and assign the Admin role instead.");
        }

        var user = new User(username, email, HashWithPolicy(password, passwordSubject), displayName);
        user.MarkAsSuperAdmin();
        await userRepository.InsertAsync(user, cancellationToken);

        logger.LogInformation("Host super admin created: {Username} (ID: {UserId})", user.Username, user.Id);
        return user;
    }

    /// <summary>
    /// 管理员重置他人口令（不校验原口令，由应用层完成越权判断）
    /// </summary>
    public void ResetPassword(User user, string? newPassword) =>
        user.UpdatePasswordHash(HashWithPolicy(newPassword));
#endif

#if (LocalIdentity)
    /// <summary>
    /// 创建用户
    /// </summary>
    /// <remarks>
    /// 资源服务形态没有这个方法：那一侧的用户行由 <see cref="EnsureProjectedAsync"/> 按令牌投影，
    /// 不存在"由本服务决定一个新主体的标识"这回事。
    /// </remarks>
    /// <param name="passwordSubject">
    /// 口令策略错误消息里的主体描述。内部调用（种子、租户初始化）应传入可辨识的值，
    /// 否则失败消息只会说"Password"，看不出是哪一处的口令不合规
    /// </param>
    public async Task<User> CreateUserAsync(
        string username,
        string email,
        string password,
        string? displayName,
        string passwordSubject = "Password",
        CancellationToken cancellationToken = default)
    {
        // 检查用户名唯一性
        if (!await IsUsernameAvailableAsync(username, cancellationToken))
        {
            throw new BusinessException(UserErrorCodes.UsernameTaken, $"Username '{username}' already exists.")
                .WithData("Username", username);
        }

        // 检查邮箱唯一性
        if (!await IsEmailAvailableAsync(email, cancellationToken))
        {
            throw new BusinessException(UserErrorCodes.EmailTaken, $"Email '{email}' is already in use.")
                .WithData("Email", email);
        }

        // 创建用户
        var user = new User(username, email, HashWithPolicy(password, passwordSubject), displayName);
        await userRepository.InsertAsync(user, cancellationToken);

        logger.LogInformation("User created: {Username} (ID: {UserId})", user.Username, user.Id);
        return user;
    }

#endif

    /// <summary>
    /// 更新个人信息
    /// </summary>
    public async Task UpdateProfileAsync(
        User user,
        string username,
        string email,
        string? displayName,
        string? phoneNumber,
        CancellationToken cancellationToken = default)
    {
        // 检查用户名唯一性
        if (!await IsUsernameAvailableAsync(user.Id, username, cancellationToken))
        {
            throw new BusinessException(UserErrorCodes.UsernameTaken, $"Username '{username}' already exists.")
                .WithData("Username", username);
        }

        // 检查邮箱唯一性
        if (!await IsEmailAvailableAsync(user.Id, email, cancellationToken))
        {
            throw new BusinessException(UserErrorCodes.EmailTaken, $"Email '{email}' is already in use.")
                .WithData("Email", email);
        }

        user.UpdateProfile(username, email, displayName, phoneNumber);
    }

#if (LocalIdentity)
    /// <summary>
    /// 修改密码
    /// </summary>
    public Task ChangePasswordAsync(
        User user,
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken = default)
    {
        if (user.PasswordHash == null)
        {
            throw new BusinessException(SecurityErrorCodes.LocalPasswordNotSet, "The current account has no local password set and cannot change the password.")
                ;
        }

        if (!passwordHasher.VerifyPassword(user.PasswordHash, currentPassword))
        {
            throw new BusinessException(SecurityErrorCodes.CurrentPasswordIncorrect, "The current password is incorrect.")
                ;
        }

        user.UpdatePasswordHash(HashWithPolicy(newPassword, "New password"));
        return Task.CompletedTask;
    }

    public async Task<User> CreateUserWithRolesAsync(
        string username,
        string email,
        string password,
        string? displayName,
        List<Guid>? roleIds,
        CancellationToken cancellationToken = default)
    {
        var user = await CreateUserAsync(
            username, email, password, displayName, cancellationToken: cancellationToken);

        // 分配角色
        if (roleIds != null && roleIds.Count > 0)
        {
            await AssignRolesToUserAsync(user.Id, roleIds, cancellationToken);
        }

        return user;
    }

    /// <summary>
    /// 为用户分配角色
    /// </summary>
    public async Task AssignRolesToUserAsync(Guid userId, List<Guid> roleIds, CancellationToken cancellationToken = default)
    {
        var userRoles = roleIds.Select(roleId => new UserRole(userId, roleId)).ToList();
        await userRoleRepository.InsertManyAsync(userRoles, cancellationToken);
    }

    /// <summary>
    /// 为用户分配默认角色
    /// </summary>
    /// <returns>本次分配的角色名称；没有默认角色时为空。</returns>
    /// <remarks>
    /// 回传角色名而不是让调用方回查：在工作单元内这些关联行还没落库，
    /// <see cref="GetUserRoleNamesAsync"/> 查不到。调用方要的本就是"我刚分配的那些"。
    /// </remarks>
    public async Task<List<string>> AssignDefaultRolesToUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var defaultRoles = (await roleRepository.GetListAsync(r => r.IsDefault, cancellationToken)).ToList();

        if (defaultRoles.Count == 0)
        {
            logger.LogWarning("No default roles found; user {UserId} was not assigned any role", userId);
            return [];
        }

        var userRoles = defaultRoles.Select(role => new UserRole(userId, role.Id)).ToList();
        await userRoleRepository.InsertManyAsync(userRoles, cancellationToken);

        logger.LogInformation("User {UserId} was assigned {Count} default role(s)", userId, defaultRoles.Count);
        return [.. defaultRoles.Select(role => role.Name)];
    }

    /// <summary>
    /// 获取用户的角色名称列表
    /// </summary>
    public async Task<List<string>> GetUserRoleNamesAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var userRoles = await userRoleRepository.GetListAsync(ur => ur.UserId == userId, cancellationToken);
        var roleIds = userRoles.Select(ur => ur.RoleId).ToList();

        if (roleIds.Count == 0)
        {
            return [];
        }

        var roles = await roleRepository.GetListAsync(r => roleIds.Contains(r.Id), cancellationToken);
        return roles.Select(r => r.Name).ToList();
    }

    /// <summary>
    /// 校验用户名密码，并按 <paramref name="lockout"/> 累计失败、触发锁定。
    /// </summary>
    /// <remarks>
    /// <para>锁定中的账号<b>不校验密码</b>，直接返回 <see cref="CredentialValidationStatus.LockedOut"/>：
    /// 锁定期间若仍按密码对错给出不同结果，攻击者照样能一个个试，锁定就只是换了一种报错。</para>
    /// <para>没有密码的账号（只经外部登录）输错不计数：那里没有可猜的密码，
    /// 计数只会让别人能把它锁住，连外部登录一起挡在外面。</para>
    /// </remarks>
    public async Task<CredentialValidationResult> ValidateCredentialsAsync(
        string usernameOrEmail,
        string password,
        LoginLockoutPolicy lockout,
        DateTime now,
        CancellationToken cancellationToken = default)
    {
        var user = await userRepository.GetFirstAsync(
            u => u.Username == usernameOrEmail || u.Email == usernameOrEmail,
            q => q.OrderBy(u => u.Id),
            cancellationToken);

        if (user == null || user.PasswordHash == null)
        {
            return new CredentialValidationResult(CredentialValidationStatus.InvalidCredentials, user);
        }

        if (user.GetAccessStatus(now) == UserAccessStatus.LockedOut)
        {
            return new CredentialValidationResult(CredentialValidationStatus.LockedOut, user);
        }

        if (passwordHasher.VerifyPassword(user.PasswordHash, password))
        {
            return new CredentialValidationResult(CredentialValidationStatus.Succeeded, user);
        }

        var lockedOut = user.RecordAccessFailed(now, lockout);
        await userRepository.UpdateAsync(user, cancellationToken);

        return lockedOut
            ? new CredentialValidationResult(CredentialValidationStatus.LockedOut, user, LockoutTriggered: true)
            : new CredentialValidationResult(CredentialValidationStatus.InvalidCredentials, user);
    }
#endif
}
