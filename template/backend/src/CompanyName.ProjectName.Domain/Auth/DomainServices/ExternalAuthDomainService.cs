#if (LocalIdentity)
using System.Text;
using CompanyName.ProjectName.Domain.Auth.Errors;
using CompanyName.ProjectName.Domain.Users.Errors;
using Leistd.Timing;
using Leistd.ExceptionHandling;
using CompanyName.ProjectName.Domain.Auth.Abstractions;
using CompanyName.ProjectName.Domain.Auth.Entities;
using CompanyName.ProjectName.Domain.Users.Constants;
using CompanyName.ProjectName.Domain.Users.Entities;
using Leistd.Auditing.Abstractions;
using Leistd.Ddd.Domain.DataFilters;
using Leistd.Ddd.Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace CompanyName.ProjectName.Domain.Auth.DomainServices;

/// <summary>
/// 外部认证领域服务
/// </summary>
public class ExternalAuthDomainService(
    IRepository<User, Guid> userRepository,
    IRepository<ExternalLoginConnection, Guid> externalLoginConnectionRepository,
    IDataFilter dataFilter,
    IClock clock,
    ILogger<ExternalAuthDomainService> logger)
{
    // 用户名由本服务生成（见 GenerateAvailableUsernameAsync）。规则本身在 UsernameRules，
    // 基底长度上限则是可读性取舍：名字再长对识别没有帮助，加上后缀仍远低于 UsernameRules.MaxLength。
    private const int UsernameBaseMaxLength = 24;
    private const int UsernameSuffixAttempts = 5;
    private const string FallbackUsernameBase = "user";

    /// <summary>
    /// 查找或创建外部登录用户
    /// </summary>
    /// <returns>
    /// 用户；以及本次是否新建了用户。新建的用户还没有任何角色，默认角色由应用层分配：
    /// 默认与否是角色聚合上的标记，领域服务不读外聚合。
    /// </returns>
    /// <remarks>
    /// <para>
    /// 按邮箱关联已有用户要求两边都已验证：提供商确认邮箱属于这个外部账号，且本地账号的邮箱也已确认。
    /// 否则任何人在提供商那里填上别人的邮箱（或抢先用别人的邮箱在本地注册）就能接管对方账号。
    /// 不满足时邮箱已被占用则拒绝，由用户先登录原账号、再在账号设置里绑定。
    /// 新建账号只采用已验证的邮箱（并记为已确认），未验证的换成占位地址。
    /// </para>
    /// </remarks>
    public async Task<(User User, bool Created)> FindOrCreateUserAsync(
        string provider,
        ExternalUserInfo externalUserInfo,
        CancellationToken cancellationToken = default)
    {
        // 先按外部连接查找，再用邮箱关联已有用户（两边都已验证才关联）。
        var connection = await externalLoginConnectionRepository.GetFirstAsync(
            c => c.Provider == provider && c.ProviderUserId == externalUserInfo.ProviderId,
            q => q.OrderBy(c => c.Id),
            cancellationToken);

        if (connection != null)
        {
            var existingUser = await userRepository.GetByIdAsync(connection.UserId, cancellationToken);
            if (existingUser == null)
            {
                throw new BusinessException(UserErrorCodes.NotFound, $"User {connection.UserId} not found.")
                    .WithData("Id", connection.UserId);
            }

            logger.LogInformation("User {Username} signed in via {Provider}", existingUser.Username, provider);
            return (existingUser, false);
        }

        var created = false;
        User? user = null;
        if (!string.IsNullOrEmpty(externalUserInfo.Email))
        {
            // 查找要看见被软删除的行：Email 的唯一索引没有排除 IsDeleted，被删用户仍然占着地址。
            // 只按未删除的行查，这里会判定"没人用过"，随后带着同一个地址建号，落库撞唯一索引返回 500
            using (dataFilter.Disable<ISoftDelete>())
            {
                user = await userRepository.GetFirstAsync(
                    u => u.Email == externalUserInfo.Email,
                    q => q.OrderBy(u => u.Id),
                    cancellationToken);
            }

            // 被删用户不参与自动关联：登不进去的账号接不了这个外部身份，
            // 但它的地址还占着，也不能拿同一个地址另建一个
            if (user is { IsDeleted: true })
            {
                // 用专用错误码而不是 UserErrorCodes.EmailTaken：本地化形态下词条会整条替换这里的消息，
                // 而 EmailTaken 的词条只说"已被使用"，既丢掉"账号已删除"这层信息，又带一个要回显地址的占位符
                throw new BusinessException(
                        ExternalAuthErrorCodes.EmailOwnedByDeletedAccount,
                        "This email belongs to a deleted account. Ask an administrator to restore it, or use another address.")
                    .WithData("Provider", ProviderName(provider, externalUserInfo));
            }

            if (user != null && !(externalUserInfo.EmailVerified && user.EmailConfirmed))
            {
                throw new BusinessException(
                        ExternalAuthErrorCodes.AccountExistsSignInToLink,
                        $"An account with this email already exists. Sign in to it and link {ProviderName(provider, externalUserInfo)} from account settings.")
                    .WithData("Provider", ProviderName(provider, externalUserInfo));
            }
        }

        if (user == null)
        {
            // 未验证的邮箱不写进账号：写进去就占用了别人的地址，本人随后注册、登录都会被挡，
            // 找回时接手的还是挂着对方外部绑定的账号。原始邮箱只留在外部连接上
            var emailVerified = externalUserInfo.EmailVerified && !string.IsNullOrEmpty(externalUserInfo.Email);

            // 用户名不取提供商给的账号标签：Google 那边标签就是邮箱，拿它当用户名会把半个联系方式
            // 变成公开标识符，而且不同域的同名用户会撞上 Username 的唯一索引。
            // 基底优先取提供商的公开句柄（GitHub 的 login），没有句柄时用显示名派生。
            var username = await GenerateAvailableUsernameAsync(
                externalUserInfo.SuggestedUsername ?? externalUserInfo.DisplayName,
                cancellationToken);

            user = new User(
                username: username,
                // 占位地址按构造唯一，不由任何业务值派生：用户名可以改、外部连接可以解绑，
                // 而用户行连同它的邮箱一直在（软删除也还占着唯一索引）。
                // 由用户名或 providerId 派生都会让下一个人算出同一个地址，撞上 Email 的唯一索引
                email: emailVerified ? externalUserInfo.Email! : PlaceholderEmail(provider),
                passwordHash: null,
                displayName: externalUserInfo.DisplayName ?? username
            );
            if (emailVerified)
            {
                // 提供商已确认：本人再用别的提供商（同一已验证邮箱）登录时能关联回来
                user.ConfirmEmail();
            }

            if (!string.IsNullOrEmpty(externalUserInfo.AvatarUrl))
            {
                user.Update(user.DisplayName, user.PhoneNumber, externalUserInfo.AvatarUrl);
            }

            await userRepository.InsertAsync(user, cancellationToken);
            logger.LogInformation("Created a new user via {Provider}: {Username}", provider, user.Username);
            created = true;
        }

        var newConnection = new ExternalLoginConnection(
            userId: user.Id,
            provider: provider,
            providerUserId: externalUserInfo.ProviderId,
            syncedAt: clock.Now,
            providerAccountLabel: externalUserInfo.ProviderAccountLabel,
            providerEmail: externalUserInfo.Email,
            providerAvatarUrl: externalUserInfo.AvatarUrl
        );
        await externalLoginConnectionRepository.InsertAsync(newConnection, cancellationToken);

        logger.LogInformation("Linked user {Username} to a {Provider} login connection", user.Username, provider);

        return (user, created);
    }

    /// <summary>
    /// 把一个外部身份绑定到已登录的用户（"绑定"模式，而不是登录或建号）。
    /// </summary>
    /// <remarks>
    /// 一个外部身份只能属于一个用户：已绑在别人名下时拒绝，而不是改绑——
    /// 改绑等于让任何能登录这个外部账号的人把它从原主人那里抢走。
    /// 同一提供商每人只绑一个，界面按提供商展示，多个同类绑定说不清哪个在用。
    /// </remarks>
    public async Task<ExternalLoginConnection> LinkAsync(
        User user,
        string provider,
        ExternalUserInfo externalUserInfo,
        CancellationToken cancellationToken = default)
    {
        var existing = await externalLoginConnectionRepository.GetFirstAsync(
            c => c.Provider == provider && c.ProviderUserId == externalUserInfo.ProviderId,
            q => q.OrderBy(c => c.Id),
            cancellationToken);
        if (existing is not null && existing.UserId != user.Id)
        {
            throw new BusinessException(ExternalAuthErrorCodes.AlreadyLinked, $"This {ProviderName(provider, externalUserInfo)} account is already linked to another user.")
                .WithData("Provider", ProviderName(provider, externalUserInfo));
        }

        if (existing is not null)
        {
            existing.Update(clock.Now, externalUserInfo.ProviderAccountLabel, externalUserInfo.Email, externalUserInfo.AvatarUrl);
            await externalLoginConnectionRepository.UpdateAsync(existing, cancellationToken);
            return existing;
        }

        if (await externalLoginConnectionRepository.AnyAsync(c => c.UserId == user.Id && c.Provider == provider, cancellationToken))
        {
            throw new BusinessException(ExternalAuthErrorCodes.ProviderAlreadyLinked, $"A {ProviderName(provider, externalUserInfo)} account is already linked. Unlink it first.")
                .WithData("Provider", ProviderName(provider, externalUserInfo));
        }

        var connection = new ExternalLoginConnection(
            userId: user.Id,
            provider: provider,
            providerUserId: externalUserInfo.ProviderId,
            syncedAt: clock.Now,
            providerAccountLabel: externalUserInfo.ProviderAccountLabel,
            providerEmail: externalUserInfo.Email,
            providerAvatarUrl: externalUserInfo.AvatarUrl);
        await externalLoginConnectionRepository.InsertAsync(connection, cancellationToken);

        logger.LogInformation("User {Username} linked a {Provider} login connection", user.Username, provider);
        return connection;
    }

    /// <summary>
    /// 解绑用户的一个外部身份。
    /// </summary>
    /// <remarks>
    /// 必须还剩一种登录方式（设有密码，或还有别的绑定），否则解绑之后这个账号就再也登不进来了。
    /// </remarks>
    /// <returns>被解绑的连接；不存在或不属于该用户时为 null。</returns>
    public async Task<ExternalLoginConnection?> UnlinkAsync(
        User user,
        Guid connectionId,
        CancellationToken cancellationToken = default)
    {
        var connection = await externalLoginConnectionRepository.GetByIdAsync(connectionId, cancellationToken);
        if (connection is null || connection.UserId != user.Id)
            return null;

        var otherLinks = await externalLoginConnectionRepository.CountAsync(
            c => c.UserId == user.Id && c.Id != connectionId,
            cancellationToken);
        if (user.PasswordHash is null && otherLinks == 0)
        {
            throw new BusinessException(ExternalAuthErrorCodes.LastSignInMethod, "This is your only way to sign in. Set a password or link another account first.");
        }

        await externalLoginConnectionRepository.DeleteAsync(connection, cancellationToken);
        // 凭据变了：此前用这个外部账号完成第一步、尚待第二步的登录挑战随之作废
        user.RotateSecurityStamp();
        logger.LogInformation("User {Username} unlinked a {Provider} login connection", user.Username, connection.Provider);
        return connection;
    }

    /// <summary>
    /// 按候选基底生成一个可用的本地用户名（<c>alice</c>，已被占用时 <c>alice_418203</c>）
    /// </summary>
    /// <param name="preferredBase">
    /// 期望的基底，取提供商的公开句柄或显示名；为空或清洗后不可用时回落到 <c>user</c>。
    /// <b>不要传邮箱或邮箱本地部</b>：用户名是公开标识符，那样等于把半个联系方式公开。
    /// </param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>当前未被占用、且符合 <see cref="UsernameRules"/> 的用户名。</returns>
    /// <exception cref="BusinessException">连续若干次随机后缀都被占用。</exception>
    /// <remarks>
    /// 外部登录建用户必须经这里拿用户名，不能直接用提供商给的值：不同域的同名用户
    /// （<c>alice@x.com</c> 与 <c>alice@y.com</c>）会撞上 <c>Username</c> 的唯一索引，第二个人首次登录直接失败；
    /// 提供商给的值还可能含 <c>.</c> <c>+</c> 这类字符，不符合 <see cref="UsernameRules"/>，
    /// 会造出用户自己在账号设置里都改不回去的名字。
    /// <para>
    /// 裸名优先是为了可读：句柄或显示名本身就是用户认得的名字。
    /// 回落基底不发裸名：显示名清洗后不可用（纯中文、纯符号）的用户全都归到同一个 <c>user</c>，
    /// 裸名既没有任何识别价值，又会成为所有这类用户的确定性争抢点。六位随机后缀把它们分散到九十万个坑位上，
    /// 空间仍然有限——分配失败的出口一直留着。
    /// </para>
    /// <para>
    /// 随机候选也可能已被占用，所以最多尝试五次：同一基底已占用 N 个后缀时单次碰撞率约 N/900000，
    /// 一个租户积累上万个 <c>user_*</c> 后就不再是可以忽略的量。
    /// </para>
    /// <para>
    /// 并发边界：可用性是先查后插，而仓储在工作单元内不立即保存，真正落库在提交时。
    /// 两个请求同时判定同一个裸基底可用时，仍会有一个在提交时撞唯一索引。
    /// 这里不为它加提交期重试：撞上要求两人同时<b>首次</b>登录且基底相同，基底一旦被占用后续都走随机后缀。
    /// </para>
    /// <para>
    /// 查重要看见被软删除的行：唯一索引没有排除 <c>IsDeleted</c>，软删除的用户仍然占着名字，
    /// 而仓储默认过滤掉它们——不关掉过滤就会生成一个数据库拒绝的名字。
    /// </para>
    /// </remarks>
    private async Task<string> GenerateAvailableUsernameAsync(
        string? preferredBase,
        CancellationToken cancellationToken = default)
    {
        var preferred = NormalizeUsernameBase(preferredBase);
        var baseName = preferred ?? FallbackUsernameBase;

        using (dataFilter.Disable<ISoftDelete>())
        {
            if (preferred is not null && !await IsUsernameTakenAsync(preferred, cancellationToken))
            {
                return preferred;
            }

            for (var attempt = 0; attempt < UsernameSuffixAttempts; attempt++)
            {
                var candidate = $"{baseName}_{Random.Shared.Next(100_000, 1_000_000)}";
                if (!await IsUsernameTakenAsync(candidate, cancellationToken))
                {
                    return candidate;
                }
            }
        }

        // 不复用 UserErrorCodes.UsernameTaken：那条的词条是"用户名已存在"并回显用户名，
        // 而这里的用户名是本服务生成的，用户既没填过它，也改不了它，只能重试。
        throw new BusinessException(
            ExternalAuthErrorCodes.UsernameAllocationFailed,
            "Could not allocate a username for this account. Try again.");
    }

    private Task<bool> IsUsernameTakenAsync(string username, CancellationToken cancellationToken) =>
        userRepository.AnyAsync(u => u.Username == username, cancellationToken);

    // 清洗成 UsernameRules 允许的字符集：只留字母数字与下划线，其余折成单个下划线并去掉首尾与连续的下划线。
    // 清洗后短于下限的返回 null，由调用方回落——补位凑长度只会造出 "zh__" 这种既不可读也没意义的名字。
    private static string? NormalizeUsernameBase(string? preferred)
    {
        if (string.IsNullOrWhiteSpace(preferred))
        {
            return null;
        }

        var builder = new StringBuilder(preferred.Length);
        foreach (var ch in preferred)
        {
            if (char.IsAsciiLetterOrDigit(ch))
            {
                builder.Append(char.ToLowerInvariant(ch));
            }
            else if (builder.Length > 0 && builder[^1] != '_')
            {
                builder.Append('_');
            }
        }

        var cleaned = builder.ToString().Trim('_');
        if (cleaned.Length > UsernameBaseMaxLength)
        {
            cleaned = cleaned[..UsernameBaseMaxLength].TrimEnd('_');
        }

        return cleaned.Length >= UsernameRules.MinLength ? cleaned : null;
    }

    // 邮箱在本地身份形态下必填且租户内唯一，但外部账号可能没有已验证邮箱，只能填占位地址。
    // 它没有读取方（要查"是哪个外部账号"看 ExternalLoginConnection），所以不需要可读、
    // 更不能由业务值派生：用户名能改、外部连接能解绑，派生出来的地址会和残留的用户行撞唯一索引。
    private static string PlaceholderEmail(string provider) =>
        // 提供商只放域名部分：本地部有 64 字节上限（RFC 5321 §4.5.3.1.1），
        // 提供商名称来自业务扩展，长度不由账号规则决定。
        // 域名标签也有 63 字符上限，同样取决于提供商取的名字
        $"{Guid.NewGuid():N}@{provider.ToLowerInvariant()}.local";

    // 面向用户的提示用提供商显示名，标识只作回退。
    private static string ProviderName(string provider, ExternalUserInfo externalUserInfo) =>
        string.IsNullOrWhiteSpace(externalUserInfo.ProviderDisplayName) ? provider : externalUserInfo.ProviderDisplayName;
}
#endif
