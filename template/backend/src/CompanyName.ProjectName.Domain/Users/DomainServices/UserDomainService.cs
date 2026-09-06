#if (LocalIdentity)
using CompanyName.ProjectName.Domain.Users.Passwords;
#endif
using CompanyName.ProjectName.Domain.Shared.Security.PasswordHash;
using CompanyName.ProjectName.Domain.Users.Entities;
using Leistd.Ddd.Domain.Repositories;
#if (LocalIdentity)
using Leistd.MultiTenancy;
#endif
using Microsoft.Extensions.Logging;
using Leistd.ExceptionHandling;
using Leistd.MultiTenancy.Abstractions;

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

    /// <summary>
    /// 创建用户
    /// </summary>
    /// <param name="passwordSubject">
    /// 口令策略错误消息里的主体描述。内部调用（种子、租户初始化）应传入可辨识的值，
    /// 否则失败消息只会说"Password"，看不出是哪一处的口令不合规
    /// </param>
    public async Task<User> CreateUserAsync(
#if (!LocalIdentity)
        Guid subjectId,
#endif
        string username,
        string email,
#if (LocalIdentity)
        string password,
#endif
        string? displayName,
#if (LocalIdentity)
        string passwordSubject = "Password",
#endif
        CancellationToken cancellationToken = default)
    {
        // 检查用户名唯一性
        if (!await IsUsernameAvailableAsync(username, cancellationToken))
        {
            throw new BadRequestException($"Username '{username}' already exists.")
#if (IncludeLocalization)
                .WithCode("User:UsernameTaken")
                .WithData("Username", username)
#endif
                ;
        }

        // 检查邮箱唯一性
        if (!await IsEmailAvailableAsync(email, cancellationToken))
        {
            throw new BadRequestException($"Email '{email}' is already in use.")
#if (IncludeLocalization)
                .WithCode("User:EmailTaken")
                .WithData("Email", email)
#endif
                ;
        }

        // 创建用户
#if (LocalIdentity)
        var user = new User(username, email, HashWithPolicy(password, passwordSubject), displayName);
#else
        var user = new User(subjectId, username, email, displayName: displayName);
#endif
        await userRepository.InsertAsync(user, cancellationToken);

        logger.LogInformation("User created: {Username} (ID: {UserId})", user.Username, user.Id);
        return user;
    }

    /// <summary>
    /// 更新个人信息
    /// </summary>
    public async Task UpdateProfileAsync(
        User user,
        string username,
        string email,
        string? displayName,
        string? phoneNumber,
        string? avatar,
        CancellationToken cancellationToken = default)
    {
        // 检查用户名唯一性
        if (!await IsUsernameAvailableAsync(user.Id, username, cancellationToken))
        {
            throw new BadRequestException($"Username '{username}' already exists.")
#if (IncludeLocalization)
                .WithCode("User:UsernameTaken")
                .WithData("Username", username)
#endif
                ;
        }

        // 检查邮箱唯一性
        if (!await IsEmailAvailableAsync(user.Id, email, cancellationToken))
        {
            throw new BadRequestException($"Email '{email}' is already in use.")
#if (IncludeLocalization)
                .WithCode("User:EmailTaken")
                .WithData("Email", email)
#endif
                ;
        }

        user.UpdateProfile(username, email, displayName, phoneNumber, avatar);
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
            throw new BadRequestException("The current account has no local password set and cannot change the password.")
#if (IncludeLocalization)
                .WithCode("Security:LocalPasswordNotSet")
#endif
                ;
        }

        if (!passwordHasher.VerifyPassword(user.PasswordHash, currentPassword))
        {
            throw new BadRequestException("The current password is incorrect.")
#if (IncludeLocalization)
                .WithCode("Security:CurrentPasswordIncorrect")
#endif
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
    /// 验证用户凭据
    /// </summary>
    public async Task<User?> ValidateCredentialsAsync(
        string usernameOrEmail,
        string password,
        CancellationToken cancellationToken = default)
    {
        var user = await userRepository.GetFirstAsync(
            u => u.Username == usernameOrEmail || u.Email == usernameOrEmail,
            q => q.OrderBy(u => u.Id),
            cancellationToken);

        if (user == null)
        {
            return null;
        }

        if (user.PasswordHash == null || !passwordHasher.VerifyPassword(user.PasswordHash, password))
        {
            return null;
        }

        return user;
    }
#endif
}
