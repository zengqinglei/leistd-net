using CompanyName.ProjectName.Application.Auth.SignIn;
#if (LocalIdentity)
using CompanyName.ProjectName.Application.Auth.Errors;
#endif
using CompanyName.ProjectName.Domain.Users.Errors;
using CompanyName.ProjectName.Application.Auth.TwoFactor;
using Leistd.UnitOfWork.Attributes;
using CompanyName.ProjectName.Application.Auth.Constants;
using CompanyName.ProjectName.Application.OperationRecords.Provider;
using CompanyName.ProjectName.Application.Users.Mappings;
using Leistd.OperationRecords.Definitions;
using Leistd.OperationRecords.Models;
using Leistd.OperationRecords.Queries;
using Leistd.OperationRecords.Recording;
using Leistd.OperationRecords.Stores;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Caching.Distributed;
using Leistd.ObjectMapping.Abstractions;
using CompanyName.ProjectName.Application.Auth.Dtos;
using CompanyName.ProjectName.Application.Auth.Policies;
using CompanyName.ProjectName.Domain.Users.DomainServices;
using CompanyName.ProjectName.Domain.Users.Entities;
using Leistd.Ddd.Application.AppServices;
using Leistd.Ddd.Domain.Repositories;
using Leistd.Security.Users;
using Microsoft.Extensions.Logging;

using CompanyName.ProjectName.Domain.Users.Options;
using CompanyName.ProjectName.Domain.Users.ValueObjects;
using CompanyName.ProjectName.Domain.Users.Policies;
using CompanyName.ProjectName.Application.Auth.SecurityAlerts;
using CompanyName.ProjectName.Application.Auth.Sessions;
using CompanyName.ProjectName.Domain.Auth.DomainServices;
using Leistd.MultiTenancy.ConnectionStrings;
using Leistd.MultiTenancy.Context;
using Leistd.MultiTenancy.Errors;
using Leistd.MultiTenancy.Management;
using Leistd.MultiTenancy.Tenancy;
using Leistd.Timing;
using Leistd.Lock.Abstractions;
using System.Security.Claims;
using Leistd.ExceptionHandling;

namespace CompanyName.ProjectName.Application.Auth.AppServices;

internal sealed class AuthAppService(
    IRepository<User, Guid> userRepository,
    UserDomainService userDomainService,
    ICurrentUser currentUser,
    ICaptchaAppService captchaAppService,
    IEmailVerificationAppService emailVerificationAppService,
    SessionSignInService sessionSignInService,
    UserSessionDomainService userSessionDomainService,
    IUserSessionAppService userSessionAppService,
    TwoFactorChallengeStore twoFactorChallengeStore,
    TwoFactorDomainService twoFactorDomainService,
    IOperationRecorder operationRecorder,
    IDistributedCache distributedCache,
    IObjectMapper objectMapper,
    IUserRegistrationPolicyProvider registrationPolicy,
    ILoginSecurityPolicyProvider loginSecurityPolicy,
    ISecurityAlertPublisher securityAlerts,
    ICurrentTenant currentTenant,
    IClock clock,
    IDistributedLock distributedLock,
    ILogger<AuthAppService> logger) : BaseAppService(), IAuthAppService
{
    public async Task<SessionLoginResult> AuthenticateSessionAsync(
        LoginInputDto input,
        CancellationToken cancellationToken = default)
    {
        var policy = await loginSecurityPolicy.GetAsync(cancellationToken);
        var now = clock.Now;
        var result = await userDomainService.ValidateCredentialsAsync(
            input.UsernameOrEmail,
            input.Password,
            policy.Lockout,
            now,
            cancellationToken);

        if (result.Status == CredentialValidationStatus.LockedOut)
        {
            var lockedUser = result.User!;
            // 锁定期间的每次尝试都记一条的话，又是一个匿名刷表的面
            if (result.LockoutTriggered)
            {
                await RecordLockedOutAsync(lockedUser, policy, cancellationToken);
            }

            throw SessionSignInService.LockedOut(lockedUser, now);
        }

        if (result.Status == CredentialValidationStatus.InvalidCredentials)
        {
            // 目标取被尝试的标识，而不是操作人——此刻没有主体。"谁在被试"正是这条记录的价值，
            // 也是"有人在爆破"唯一能被看出来的地方。用户输入受 MaxTargetIdLength 截断保护。
            //
            // 这是一个**匿名写入面**：框架在 RecordDeniedOperationAsync 里把"匿名一律不记"
            // 写成了安全属性，理由是那会让审计表变成不需要凭据的写入面。此处之所以可以记，
            // 是因为攻击面从"整个 API"收敛到了这一个端点，**并且下面按阈值节流**——
            // 全仓没有任何限流（无 AddRateLimiter），不节流就是一个无需凭据即可刷爆审计表的洞。
            var attempts = await TrackFailedLoginAsync(input.UsernameOrEmail, cancellationToken);
            if (ShouldRecordFailedLogin(attempts))
            {
                await operationRecorder.RecordFailedAsync(
                    OperationRecordActions.AuthLoginFailed,
                    OperationTarget.For(input.UsernameOrEmail),
                    OperationRecordAuthorizations.CredentialsPresented,
                    OperationFailure.FromCode(
                        "Auth:InvalidCredentials",
                        $$"""{"attempts":{{attempts}},"windowMinutes":{{FailedLoginWindowMinutes}}}"""));
            }

            throw new BusinessException(AuthErrorCodes.InvalidCredentials, "The username or password is incorrect.");
        }

        var user = result.User!;
        var outcome = await sessionSignInService.StartAsync(user, cancellationToken: cancellationToken);

        // 还要第二步时先不记成功：密码对了不等于登录成功，第二步通过时再记（见 CompleteTwoFactorLoginAsync）
        if (outcome.Principal is not null)
        {
            await RecordLoginSucceededAsync(user, cancellationToken);
        }

        return outcome;
    }

    /// <summary>
    /// 登录第二步：校验验证码或恢复码，通过后签发会话。
    /// </summary>
    /// <remarks>
    /// <para>锁定中的账号不校验验证码，与第一步同理：否则锁定期间照样能一个个试。</para>
    /// <para>输错计入账号的登录失败次数：第二步的 10⁶ 空间只靠单个挑战的尝试上限挡不住——
    /// 知道密码的人可以反复走第一步领新挑战。</para>
    /// </remarks>
    public async Task<ClaimsPrincipal> CompleteTwoFactorLoginAsync(
        TwoFactorLoginInputDto input,
        CancellationToken cancellationToken = default)
    {
        var challenge = await twoFactorChallengeStore.GetAsync(input.Token, cancellationToken);
        if (challenge is null || challenge.TenantId != currentTenant.Id)
        {
            throw TwoFactorChallengeExpired();
        }

        // 同一用户的第二步串行执行，与验证码挑战同一做法：输错次数记在缓存里，
        // 并发请求会读到同一个次数，不加锁就能在一个挑战上试出远超上限的次数。锁内重读挑战
        await using var userLock = await distributedLock.LockAsync(
            $"MyProject:2fa-login:lock:{challenge.UserId:N}", cancellationToken);
        challenge = await twoFactorChallengeStore.GetAsync(input.Token, cancellationToken);
        var user = challenge is not null
            ? await userRepository.GetByIdAsync(challenge.UserId, cancellationToken)
            : null;
        if (challenge is null || user is null)
        {
            throw TwoFactorChallengeExpired();
        }

        var now = clock.Now;
        try
        {
            SessionSignInService.EnsureAllowed(user, now);
        }
        catch
        {
            await twoFactorChallengeStore.RemoveAsync(input.Token, cancellationToken);
            throw;
        }

        bool verified;
        var usedRecoveryCode = false;
        if (!string.IsNullOrWhiteSpace(input.RecoveryCode))
        {
            verified = usedRecoveryCode = twoFactorDomainService.UseRecoveryCode(user, input.RecoveryCode);
        }
        else if (!string.IsNullOrWhiteSpace(input.Code))
        {
            verified = twoFactorDomainService.VerifyCode(user, input.Code, now);
        }
        else
        {
            throw new BusinessException(AuthErrorCodes.TwoFactorCodeRequired, "Enter the verification code or a recovery code.")
                ;
        }

        if (!verified)
        {
            var policy = await loginSecurityPolicy.GetAsync(cancellationToken);
            var lockedOut = user.RecordAccessFailed(now, policy.Lockout);
            await userRepository.UpdateAsync(user, cancellationToken);

            if (lockedOut)
            {
                await twoFactorChallengeStore.RemoveAsync(input.Token, cancellationToken);
                await RecordLockedOutAsync(user, policy, cancellationToken);
                throw SessionSignInService.LockedOut(user, now);
            }

            if (!await twoFactorChallengeStore.RecordFailureAsync(input.Token, challenge, cancellationToken))
            {
                throw TwoFactorChallengeExpired();
            }

            throw new BusinessException(AuthErrorCodes.TwoFactorCodeInvalid, "The verification code is incorrect.");
        }

        await twoFactorChallengeStore.RemoveAsync(input.Token, cancellationToken);
        // 用掉的步与恢复码必须落库，否则同一个码还能再用一次
        await userRepository.UpdateAsync(user, cancellationToken);

        if (usedRecoveryCode)
        {
            await operationRecorder.RecordSucceededAsync(
                OperationRecordActions.AuthTwoFactorRecoveryCodeUsed,
                OperationTarget.For(user.Id, user.DisplayName ?? user.Username),
                OperationRecordAuthorizations.CredentialsPresented,
                cancellationToken);
        }

        await RecordLoginSucceededAsync(user, cancellationToken);
        return await sessionSignInService.SignInAsync(user, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// 换发当前会话：结束现在这个，按账号最新状态签发一个新的。
    /// </summary>
    /// <remarks>受限会话的用户完成两步验证设置后调用，换掉带限制声明的那一个。</remarks>
    public async Task<ClaimsPrincipal> ReissueSessionAsync(CancellationToken cancellationToken = default)
    {
        var user = await GetCurrentUserEntityAsync(cancellationToken);
        await userSessionAppService.EndCurrentSessionAsync(cancellationToken);
        return await sessionSignInService.SignInAsync(user, cancellationToken: cancellationToken);
    }

    // 登录成功的"什么人"由目标承载：SignInAsync 只往响应里种 Cookie，
    // 本次请求的主体仍是匿名，操作人字段为空是诚实的。
    private Task RecordLoginSucceededAsync(User user, CancellationToken cancellationToken) =>
        operationRecorder.RecordSucceededAsync(
            OperationRecordActions.AuthLoginSucceeded,
            OperationTarget.For(user.Id, user.DisplayName ?? user.Username),
            OperationRecordAuthorizations.CredentialsPresented,
            cancellationToken);

    // 只在"这一次恰好触发锁定"时记：一个锁定期内至多一条，写入量有界。本人同时收到一条安全提醒
    private async Task RecordLockedOutAsync(User user, LoginSecurityPolicy policy, CancellationToken cancellationToken)
    {
        await securityAlerts.PublishAsync(
            user.Id,
            new SecurityAlert(SecurityAlertKind.LockedOut, Until: user.LockoutEnd),
            cancellationToken);
        await operationRecorder.RecordFailedAsync(
            OperationRecordActions.AuthLockedOut,
            OperationTarget.For(user.Id, user.DisplayName ?? user.Username),
            OperationRecordAuthorizations.CredentialsPresented,
            OperationFailure.FromCode(
                "Auth:UserTemporarilyLockedOut",
                $$"""{"maxFailedAttempts":{{policy.Lockout.MaxFailedAttempts}},"minutes":{{(int)policy.Lockout.Duration.TotalMinutes}}}"""));
    }

    private static BusinessException TwoFactorChallengeExpired() =>
        new BusinessException(AuthErrorCodes.TwoFactorChallengeExpired, "The sign-in attempt has expired. Sign in again.")
        ;

    /// <summary>失败登录的计数窗口（分钟）。</summary>
    private const int FailedLoginWindowMinutes = 5;

    /// <summary>
    /// 在这些累计次数上落一条记录；之后每 100 次再落一条。
    /// </summary>
    /// <remarks>
    /// <b>这是节流，不是"先记后合并"。</b><c>IOperationRecordStore</c> 刻意没有更新能力
    /// （append-only 是它写在契约里的设计），所以做不到逐次记录再归并。
    /// 阈值写入让首次失败立刻可见、升级过程可见，同时把单个标识的写入量从 O(n) 降到 O(log n)。
    /// <para><b>代价</b>：窗口内非阈值的那些失败不各自成行，精确时刻会丢。
    /// 它能回答"某标识在某窗口内失败了 N 次"，不能回答"第 7 次发生在几点"。
    /// 对"谁在爆破"够用，对逐次取证不够——需要后者时按 CorrelationId 去请求日志里查。</para>
    /// </remarks>
    private static readonly int[] FailedLoginRecordThresholds = [1, 5, 25, 100];

    private static bool ShouldRecordFailedLogin(int attempts)
        => Array.IndexOf(FailedLoginRecordThresholds, attempts) >= 0
           || (attempts > 100 && attempts % 100 == 0);

    /// <summary>
    /// 累计同一标识在窗口内的失败次数，返回本次之后的累计值。
    /// </summary>
    /// <remarks>
    /// <b>刻意不加分布式锁。</b><c>CaptchaAppService</c> 为"读取-消费-比较"的原子性用了锁，
    /// 那里丢一次就是一次可被重放的验证码；这里丢一次递增只会让某个阈值记录晚一点出现，
    /// 不影响"有人在爆破"这个信号。为一个节流计数器去抢分布式锁，代价大于收益。
    /// <para>缓存键对标识做哈希：它是<b>用户输入</b>，直接拼进键会把任意内容带进缓存键空间。</para>
    /// </remarks>
    private async Task<int> TrackFailedLoginAsync(string identifier, CancellationToken cancellationToken)
    {
        var key = FailedLoginCacheKey(identifier);
        var current = await distributedCache.GetStringAsync(key, cancellationToken);
        var attempts = int.TryParse(current, out var parsed) ? parsed + 1 : 1;

        await distributedCache.SetStringAsync(
            key,
            attempts.ToString(System.Globalization.CultureInfo.InvariantCulture),
            new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(FailedLoginWindowMinutes)
            },
            cancellationToken);

        return attempts;
    }

    private static string FailedLoginCacheKey(string identifier)
    {
        var normalized = identifier.Trim().ToLowerInvariant();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return $"auth:login-failures:{Convert.ToHexString(hash)}";
    }

    /// <remarks>建用户与分配默认角色必须同生共死：拆开后注册失败会留下没有任何角色的用户。</remarks>
    [UnitOfWork]
    public async Task<UserOutputDto> RegisterAsync(RegisterInputDto input, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Registering user {Username} with email {Email}", input.Username, input.Email);

        var options = await registrationPolicy.GetAsync(cancellationToken);

        if (options.EnableEmailVerification)
        {
            if (input.EmailVerification is null || input.EmailVerification.ChallengeId == Guid.Empty)
            {
                throw new BusinessException(AuthErrorCodes.EmailCodeRequired, "Please enter the email verification code.")
                    ;
            }

            var isValidEmailCode = await emailVerificationAppService.ValidateEmailChallengeAsync(
                input.Email,
                input.EmailVerification,
                cancellationToken);
            if (!isValidEmailCode)
            {
                throw new BusinessException(AuthErrorCodes.EmailCodeInvalid, "The email verification code is incorrect or has expired.")
                    ;
            }
        }
        else
        {
            var isValidCaptcha = await captchaAppService.ValidateCaptchaAsync(input.CaptchaToken ?? string.Empty, input.CaptchaCode ?? string.Empty, cancellationToken);
            if (!isValidCaptcha)
            {
                throw new BusinessException(AuthErrorCodes.CaptchaInvalid, "The image captcha is incorrect or has expired.")
                    ;
            }
        }

        var user = await userDomainService.CreateUserAsync(
            input.Username, input.Email, input.Password, input.DisplayName,
            cancellationToken: cancellationToken);
        var roleNames = await userDomainService.AssignDefaultRolesToUserAsync(user.Id, cancellationToken);

        logger.LogInformation("User registered (ID: {Id})", user.Id);

        // 跟随本方法的 [UnitOfWork]：注册回滚则记录一并回滚，不留"记了但没发生"的假账。
        await operationRecorder.RecordSucceededAsync(
            OperationRecordActions.AuthRegistered,
            OperationTarget.For(user.Id, user.DisplayName ?? user.Username),
            OperationRecordAuthorizations.SelfRegistration,
            cancellationToken);

        return ToOutput(user, roleNames);
    }

    /// <summary>
    /// 获取当前用户信息
    /// </summary>
    public async Task<UserOutputDto> GetCurrentUserAsync(CancellationToken cancellationToken = default)
    {
        var userId = currentUser.Id!.Value;
        var output = await GetCurrentUserOutputAsync(userId, cancellationToken);
        // 受限会话由会话声明而不是账号字段决定：同一个账号换个没开强制的租户登录就不受限
        return output with { TwoFactorSetupRequired = currentUser.FindClaim(TwoFactorClaimTypes.SetupRequired) is not null };
    }

    /// <summary>
    /// 更新个人信息
    /// </summary>
    public async Task<UserOutputDto> UpdateCurrentUserAsync(UpdateCurrentUserInputDto input, CancellationToken cancellationToken = default)
    {
        var userId = currentUser.Id!.Value;
        var user = await userRepository.GetByIdAsync(userId, cancellationToken);
        if (user == null)
        {
            throw new BusinessException(UserErrorCodes.NotFound, $"User {userId} not found.")
                .WithData("Id", userId);
        }

        logger.LogInformation("Updating current user profile (ID: {UserId})", user.Id);

        await userDomainService.UpdateProfileAsync(
            user,
            input.Username,
            input.Email,
            input.DisplayName,
            input.PhoneNumber,
            cancellationToken);

        await userRepository.UpdateAsync(user, cancellationToken);
        logger.LogInformation("Current user profile updated (ID: {UserId})", user.Id);

        return await GetCurrentUserOutputAsync(user.Id, cancellationToken);
    }

    /// <summary>
    /// 修改密码，并撤销除当前以外的全部会话
    /// </summary>
    public async Task ChangePasswordAsync(ChangePasswordInputDto input, CancellationToken cancellationToken = default)
    {
        var userId = currentUser.Id!.Value;
        var user = await userRepository.GetByIdAsync(userId, cancellationToken);
        if (user == null)
        {
            throw new BusinessException(UserErrorCodes.NotFound, $"User {userId} not found.")
                .WithData("Id", userId);
        }

        logger.LogInformation("Changing current user password (ID: {UserId})", user.Id);

        await userDomainService.ChangePasswordAsync(user, input.CurrentPassword, input.NewPassword, cancellationToken);
        await userRepository.UpdateAsync(user, cancellationToken);

        // 凭据换了，以旧密码建立的其他会话随之失效；发起修改的这台保留，免得改完密码自己也被踢出去
        await userSessionDomainService.RevokeAllAsync(user.Id, currentUser.GetSessionId(), cancellationToken);
        await securityAlerts.PublishAsync(user.Id, new SecurityAlert(SecurityAlertKind.PasswordChanged), cancellationToken);

        logger.LogInformation("Current user password changed (ID: {UserId})", user.Id);

        // 改自己的密码：此刻主体已建立，操作人字段有值，目标即本人。
        await operationRecorder.RecordSucceededAsync(
            OperationRecordActions.AuthPasswordChanged,
            OperationTarget.For(user.Id, user.DisplayName ?? user.Username),
            OperationRecordAuthorizations.AuthenticatedSelf,
            cancellationToken);
    }

    /// <summary>
    /// 设置或清除自己的头像
    /// </summary>
    /// <remarks>
    /// 本人只能上传图片（外部地址来自外部登录提供方，不由本人随手填）；
    /// 浏览器端已裁剪缩放，这里按 <see cref="AvatarPolicy"/> 校验体积与真实类型。
    /// </remarks>
    public async Task<UserOutputDto> SetCurrentUserAvatarAsync(SetAvatarInputDto input, CancellationToken cancellationToken = default)
    {
        var user = await GetCurrentUserEntityAsync(cancellationToken);
        if (!string.IsNullOrEmpty(input.Avatar) && !AvatarPolicy.TryReadImage(input.Avatar, out _))
        {
            AvatarPolicy.EnsureValid(input.Avatar);
            // 外部地址本身合法，但不是本人上传的入口能写的东西
            throw new BusinessException(UserErrorCodes.AvatarInvalid, "The avatar must be a PNG, JPEG or WebP image.")
                ;
        }

        user.SetAvatar(input.Avatar);
        await userRepository.UpdateAsync(user, cancellationToken);

        await operationRecorder.RecordSucceededAsync(
            OperationRecordActions.AuthAvatarChanged,
            OperationTarget.For(user.Id, user.DisplayName ?? user.Username),
            OperationRecordAuthorizations.AuthenticatedSelf,
            cancellationToken);

        return await GetCurrentUserOutputAsync(user.Id, cancellationToken);
    }

    /// <summary>
    /// 给自己当前的邮箱发验证码
    /// </summary>
    public async Task<EmailVerificationChallengeOutputDto> SendCurrentUserEmailCodeAsync(CancellationToken cancellationToken = default)
    {
        var user = await GetCurrentUserEntityAsync(cancellationToken);
        if (user.EmailConfirmed)
        {
            throw new BusinessException(AuthErrorCodes.EmailAlreadyVerified, "This email address has already been verified.")
                ;
        }

        return await emailVerificationAppService.SendAccountEmailCodeAsync(user.Email, cancellationToken);
    }

    /// <summary>
    /// 用验证码确认自己当前的邮箱
    /// </summary>
    /// <remarks>
    /// 校验针对的是<b>此刻</b>账号上的邮箱：发码之后改过邮箱的话，旧验证码对新地址无效，
    /// 不能用它把一个没验证过的新地址标成已验证。
    /// </remarks>
    public async Task<UserOutputDto> ConfirmCurrentUserEmailAsync(EmailVerificationInputDto input, CancellationToken cancellationToken = default)
    {
        var user = await GetCurrentUserEntityAsync(cancellationToken);
        if (!await emailVerificationAppService.ValidateAccountEmailChallengeAsync(user.Email, input, cancellationToken))
        {
            throw new BusinessException(AuthErrorCodes.EmailCodeInvalid, "The email verification code is incorrect or has expired.")
                ;
        }

        user.ConfirmEmail();
        await userRepository.UpdateAsync(user, cancellationToken);

        await operationRecorder.RecordSucceededAsync(
            OperationRecordActions.AuthEmailVerified,
            OperationTarget.For(user.Id, user.DisplayName ?? user.Username),
            OperationRecordAuthorizations.AuthenticatedSelf,
            cancellationToken);

        return await GetCurrentUserOutputAsync(user.Id, cancellationToken);
    }

    private async Task<User> GetCurrentUserEntityAsync(CancellationToken cancellationToken)
    {
        var userId = currentUser.Id!.Value;
        return await userRepository.GetByIdAsync(userId, cancellationToken)
            ?? throw new BusinessException(UserErrorCodes.NotFound, $"User {userId} not found.")
                .WithData("Id", userId);
    }

    private async Task<UserOutputDto> GetCurrentUserOutputAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await userRepository.GetByIdAsync(userId, cancellationToken);
        if (user == null)
        {
            throw new BusinessException(UserErrorCodes.NotFound, $"User {userId} not found.")
                .WithData("Id", userId);
        }

        var roleNames = await userDomainService.GetUserRoleNamesAsync(userId, cancellationToken);

        return ToOutput(user, roleNames);
    }

    /// <remarks>
    /// 角色名由调用方给出：写路径刚分配完角色、关联行尚未落库，映射配置里的实体连接查不到。
    /// 经 <see cref="UserProfile.RoleNamesKey"/> 传入，仍走已注册的 <c>User → UserOutputDto</c> 映射。
    /// </remarks>
    private UserOutputDto ToOutput(User user, List<string> roleNames)
    {
        return objectMapper.Map<User, UserOutputDto>(
            user,
            new Dictionary<string, object> { [UserProfile.RoleNamesKey] = roleNames });
    }
}
