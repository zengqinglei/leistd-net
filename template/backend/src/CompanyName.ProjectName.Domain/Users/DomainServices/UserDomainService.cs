#if (LocalIdentity)
using CompanyName.ProjectName.Domain.Users.Policies;
using CompanyName.ProjectName.Domain.Users.ValueObjects;
#endif
using CompanyName.ProjectName.Domain.Shared.Security.Errors;
using CompanyName.ProjectName.Domain.Users.Errors;
#if (LocalIdentity)
using CompanyName.ProjectName.Domain.Shared.Security.PasswordHash;
#endif
using CompanyName.ProjectName.Domain.Users.Entities;
using Leistd.Auditing.Abstractions;
using Leistd.Ddd.Domain.DataFilters;
using Leistd.Ddd.Domain.Repositories;
#if (LocalIdentity)
using Leistd.MultiTenancy;
#endif
using Microsoft.Extensions.Logging;
using Leistd.ExceptionHandling;
using Leistd.MultiTenancy.ConnectionStrings;
using Leistd.MultiTenancy.Context;
using Leistd.MultiTenancy.Errors;
using Leistd.MultiTenancy.Tenancy;

namespace CompanyName.ProjectName.Domain.Users.DomainServices;

/// <summary>
/// 用户领域服务
/// </summary>
public class UserDomainService(
    IRepository<User, Guid> userRepository,
    IDataFilter dataFilter,
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
    /// <para>首次访问的并发不在这里处理：撞主键要到冲刷时才抛，本方法内接不到。
    /// 由持有事务边界的调用方重试一次，见 <c>ResourceUserProvisioningMiddleware</c>。</para>
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
        var (name, mail) = ProjectedIdentity(subjectId, username, email);
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

        // 这里不包 try/catch：工作单元内 InsertAsync 只登记实体、不访问数据库，
        // 撞主键要到冲刷时才抛，catch 在这里是永不触发的死代码（见工作单元文档）。
        // 并发首访由调用方重试一次收口——它持有事务边界，也只有它能重开一个干净的工作单元。
        var user = new User(subjectId, name, mail, displayName: display);
        await userRepository.InsertAsync(user, cancellationToken);

        logger.LogInformation("Projected issuer subject {SubjectId} into a local user row.", subjectId);
        return user;
    }

    /// <summary>
    /// 取得主体对应的本地用户行；还没有时建立只含主体标识的最小行，供部署引导在首次访问之前分配角色。
    /// </summary>
    /// <remarks>
    /// <para>最小行按投影的回落规则命名（用户名取主体标识、邮箱留空），不编造资料；
    /// 首次真实访问时由 <see cref="EnsureProjectedAsync"/> 按令牌补齐。</para>
    /// <para>已有用户原样返回：资料归签发方，启停归本服务，引导都不改。
    /// 已被删除的用户不恢复——删除是本服务做过的授权决定，模板不提供恢复入口，由项目自己的数据恢复流程处理。</para>
    /// </remarks>
    /// <param name="subjectId">签发方主体标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>用户行，以及它是否由本次建立。</returns>
    public async Task<(User User, bool Created)> EnsureSubjectAsync(
        Guid subjectId,
        CancellationToken cancellationToken = default)
    {
        using (dataFilter.Disable<ISoftDelete>())
        {
            var existing = await userRepository.GetByIdAsync(subjectId, cancellationToken);
            if (existing is { IsDeleted: true })
            {
                throw new InvalidOperationException(
                    $"User {subjectId} has been deleted in this service and cannot be bootstrapped; recover it through your data recovery procedure first.");
            }

            if (existing is not null)
            {
                return (existing, false);
            }
        }

        var (name, mail) = ProjectedIdentity(subjectId, username: null, email: null);
        var user = new User(subjectId, name, mail);
        await userRepository.InsertAsync(user, cancellationToken);
        logger.LogInformation("Created a minimal local user row for issuer subject {SubjectId}.", subjectId);
        return (user, true);
    }

    /// <summary>令牌缺少用户名或邮箱时的回落：用户名取主体标识，保证非空且可检索；邮箱留空串。</summary>
    private static (string Username, string Email) ProjectedIdentity(Guid subjectId, string? username, string? email)
        => (string.IsNullOrWhiteSpace(username) ? subjectId.ToString() : username.Trim(), email?.Trim() ?? string.Empty);

#endif
    /// <summary>
    /// 检查用户名是否可用
    /// </summary>
    /// <remarks>
    /// 查重要看见被软删除的行：用户名与邮箱的唯一索引都没有排除 <c>IsDeleted</c>，
    /// 软删除的用户仍然占着它们，而仓储默认把这些行过滤掉。不关掉过滤，这里会答"可用"，
    /// 随后落库撞唯一索引——用户看到的是 500，而不是"该用户名已被占用"。
    /// <para>
    /// 保留租户过滤：跨租户允许同名同邮箱，唯一索引也是按租户分开的。
    /// </para>
    /// </remarks>
    public async Task<bool> IsUsernameAvailableAsync(string username, CancellationToken cancellationToken = default)
    {
        using var _ = dataFilter.Disable<ISoftDelete>();
        return !await userRepository.AnyAsync(u => u.Username == username, cancellationToken);
    }

    /// <summary>
    /// 检查用户名是否可用（排除指定用户）
    /// </summary>
    /// <inheritdoc cref="IsUsernameAvailableAsync(string, CancellationToken)" path="/remarks"/>
    public async Task<bool> IsUsernameAvailableAsync(Guid excludeUserId, string username, CancellationToken cancellationToken = default)
    {
        using var _ = dataFilter.Disable<ISoftDelete>();
        return !await userRepository.AnyAsync(u => u.Id != excludeUserId && u.Username == username, cancellationToken);
    }

    /// <summary>
    /// 检查邮箱是否可用
    /// </summary>
    /// <inheritdoc cref="IsUsernameAvailableAsync(string, CancellationToken)" path="/remarks"/>
    public async Task<bool> IsEmailAvailableAsync(string email, CancellationToken cancellationToken = default)
    {
        using var _ = dataFilter.Disable<ISoftDelete>();
        return !await userRepository.AnyAsync(u => u.Email == email, cancellationToken);
    }

    /// <summary>
    /// 检查邮箱是否可用（排除指定用户）
    /// </summary>
    /// <inheritdoc cref="IsUsernameAvailableAsync(string, CancellationToken)" path="/remarks"/>
    public async Task<bool> IsEmailAvailableAsync(Guid excludeUserId, string email, CancellationToken cancellationToken = default)
    {
        using var _ = dataFilter.Disable<ISoftDelete>();
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
    /// 本人修改口令：校验当前口令，通过则写入新口令的哈希。
    /// </summary>
    /// <remarks>
    /// <b>失败返回状态而不是抛异常</b>，与 <see cref="ValidateCredentialsAsync"/> 同型：
    /// 当前口令不对是一次<b>再认证失败</b>，应用层要先计入失败次数、留下审计，然后才抛。
    /// 这里直接抛的话，应用层拿不到那个时机（见 <c>IReauthenticationGuard</c>）。
    /// 新口令的强度校验仍在这里，失败照常抛——那是输入不合格，不是认证失败。
    /// </remarks>
    /// <param name="user">当前用户。</param>
    /// <param name="currentPassword">待校验的当前口令。</param>
    /// <param name="newPassword">新口令。</param>
    public ChangePasswordStatus ChangePassword(User user, string currentPassword, string newPassword)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (user.PasswordHash == null)
        {
            return ChangePasswordStatus.NoLocalPassword;
        }

        if (!passwordHasher.VerifyPassword(user.PasswordHash, currentPassword))
        {
            return ChangePasswordStatus.CurrentPasswordIncorrect;
        }

        user.UpdatePasswordHash(HashWithPolicy(newPassword, "New password"));
        return ChangePasswordStatus.Succeeded;
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
    /// 校验用户名密码，只给判定。
    /// </summary>
    /// <remarks>
    /// <para><b>失败返回状态而不是抛异常，也不在这里累计失败</b>：累计要落在独立的工作单元里，
    /// 那是事务编排、属于应用层（见 <c>IAccessFailureCounter</c>）。领域只回答"口令对不对"。</para>
    /// <para>锁定中的账号<b>不校验密码</b>，直接返回 <see cref="CredentialValidationStatus.LockedOut"/>：
    /// 锁定期间若仍按密码对错给出不同结果，攻击者照样能一个个试，锁定就只是换了一种报错。</para>
    /// <para>没有密码的账号（只经外部登录）按凭据无效处理且<b>不应计数</b>：那里没有可猜的密码，
    /// 计数只会让别人能把它锁住，连外部登录一起挡在外面。调用方据
    /// <see cref="CredentialValidationResult.Countable"/> 判断。</para>
    /// </remarks>
    public async Task<CredentialValidationResult> ValidateCredentialsAsync(
        string usernameOrEmail,
        string password,
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

        return new CredentialValidationResult(CredentialValidationStatus.InvalidCredentials, user, Countable: true);
    }
#endif
}
