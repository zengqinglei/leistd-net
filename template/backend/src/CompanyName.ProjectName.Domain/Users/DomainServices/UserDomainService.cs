using CompanyName.ProjectName.Domain.Shared.Security.PasswordHash;
using CompanyName.ProjectName.Domain.Users.Entities;
using Leistd.Ddd.Domain.Repositories;
using Leistd.Exception.Core;
using Microsoft.Extensions.Logging;

namespace CompanyName.ProjectName.Domain.Users.DomainServices;

/// <summary>
/// 用户领域服务
/// </summary>
public class UserDomainService(
    IRepository<User, Guid> userRepository,
#if (IncludeRoles)
    IRepository<Role, Guid> roleRepository,
    IRepository<UserRole, Guid> userRoleRepository,
#endif
#if (IncludeIdentity)
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

    /// <summary>
    /// 创建用户
    /// </summary>
    public async Task<User> CreateUserAsync(
        string username,
        string email,
        string password,
        string? displayName,
        CancellationToken cancellationToken = default)
    {
        // 检查用户名唯一性
        if (!await IsUsernameAvailableAsync(username, cancellationToken))
        {
            throw new BadRequestException($"Username '{username}' already exists.")
#if (IncludeLocalization)
                .WithLocalization("User:UsernameTaken")
                .WithData("Username", username)
#endif
                ;
        }

        // 检查邮箱唯一性
        if (!await IsEmailAvailableAsync(email, cancellationToken))
        {
            throw new BadRequestException($"Email '{email}' is already in use.")
#if (IncludeLocalization)
                .WithLocalization("User:EmailTaken")
                .WithData("Email", email)
#endif
                ;
        }

        // 创建用户
#if (IncludeIdentity)
        var user = new User(username, email, passwordHasher.HashPassword(password), displayName);
#else
        var user = new User(username, email, displayName: displayName);
#endif
        await userRepository.InsertAsync(user, cancellationToken);

        logger.LogInformation("创建用户成功: {Username} (ID: {UserId})", user.Username, user.Id);
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
                .WithLocalization("User:UsernameTaken")
                .WithData("Username", username)
#endif
                ;
        }

        // 检查邮箱唯一性
        if (!await IsEmailAvailableAsync(user.Id, email, cancellationToken))
        {
            throw new BadRequestException($"Email '{email}' is already in use.")
#if (IncludeLocalization)
                .WithLocalization("User:EmailTaken")
                .WithData("Email", email)
#endif
                ;
        }

        user.UpdateProfile(username, email, displayName, phoneNumber, avatar);
    }

#if (IncludeIdentity)
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
                .WithLocalization("Security:LocalPasswordNotSet")
#endif
                ;
        }

        if (!passwordHasher.VerifyPassword(user.PasswordHash, currentPassword))
        {
            throw new BadRequestException("The current password is incorrect.")
#if (IncludeLocalization)
                .WithLocalization("Security:CurrentPasswordIncorrect")
#endif
                ;
        }

        user.UpdatePasswordHash(passwordHasher.HashPassword(newPassword));
        return Task.CompletedTask;
    }

#if (IncludeRoles)
    public async Task<User> CreateUserWithRolesAsync(
        string username,
        string email,
        string password,
        string? displayName,
        List<Guid>? roleIds,
        CancellationToken cancellationToken = default)
    {
        var user = await CreateUserAsync(username, email, password, displayName, cancellationToken);

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
    public async Task AssignDefaultRolesToUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var defaultRoles = (await roleRepository.GetListAsync(r => r.IsDefault, cancellationToken)).ToList();

        if (defaultRoles.Count == 0)
        {
            logger.LogWarning("未找到默认角色，用户 {UserId} 未分配任何角色", userId);
            return;
        }

        var userRoles = defaultRoles.Select(role => new UserRole(userId, role.Id)).ToList();
        await userRoleRepository.InsertManyAsync(userRoles, cancellationToken);

        logger.LogInformation("为用户 {UserId} 分配了 {Count} 个默认角色", userId, defaultRoles.Count);
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

#endif
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
