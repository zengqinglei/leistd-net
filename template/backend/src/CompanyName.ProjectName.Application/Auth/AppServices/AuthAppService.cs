using Leistd.UnitOfWork.Attributes;
using CompanyName.ProjectName.Application.OperationRecords;
using CompanyName.ProjectName.Application.Users.Mappings;
using Leistd.OperationRecords.Abstractions;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Caching.Distributed;
using Leistd.ObjectMapping.Abstractions;
using CompanyName.ProjectName.Application.Auth.Dtos;
using CompanyName.ProjectName.Application.Auth.Policies;
using CompanyName.ProjectName.Domain.Users.DomainServices;
using CompanyName.ProjectName.Domain.Users.Entities;
using Leistd.Ddd.Application.AppService;
using Leistd.Ddd.Domain.Repositories;
using Leistd.Security.Users;
using Microsoft.Extensions.Logging;

using CompanyName.ProjectName.Domain.Users.Options;
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
    IOperationRecorder operationRecorder,
    IDistributedCache distributedCache,
    IObjectMapper objectMapper,
    IUserRegistrationPolicyProvider registrationPolicy,
    ILogger<AuthAppService> logger) : BaseAppService(), IAuthAppService
{
    public async Task<ClaimsPrincipal> AuthenticateSessionAsync(
        LoginInputDto input,
        CancellationToken cancellationToken = default)
    {
        var user = await userDomainService.ValidateCredentialsAsync(
            input.UsernameOrEmail,
            input.Password,
            cancellationToken);
        if (user is null)
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
                        $$"""{"attempts":{{attempts}},"windowMinutes":{{FailedLoginWindowMinutes}}}"""),
                    cancellationToken);
            }

            throw new UnauthorizedException($"Login failed: user not found or incorrect password - {input.UsernameOrEmail}")
#if (IncludeLocalization)
                .WithCode("Auth:InvalidCredentials")
                .WithData("UsernameOrEmail", input.UsernameOrEmail)
#endif
                ;
        }

        // 同理，登录成功的"什么人"也由目标承载：SignInAsync 只往响应里种 Cookie，
        // 本次请求的主体仍是匿名，操作人字段为空是诚实的。
        await operationRecorder.RecordSucceededAsync(
            OperationRecordActions.AuthLoginSucceeded,
            OperationTarget.For(user.Id, user.DisplayName ?? user.Username),
            OperationRecordAuthorizations.CredentialsPresented,
            cancellationToken);

        return await sessionSignInService.SignInAsync(user, cancellationToken: cancellationToken);
    }

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
                throw new BadRequestException("Please enter the email verification code.")
#if (IncludeLocalization)
                    .WithCode("Auth:EmailCodeRequired")
#endif
                    ;
            }

            var isValidEmailCode = await emailVerificationAppService.ValidateEmailChallengeAsync(
                input.Email,
                input.EmailVerification,
                cancellationToken);
            if (!isValidEmailCode)
            {
                throw new BadRequestException("The email verification code is incorrect or has expired.")
#if (IncludeLocalization)
                    .WithCode("Auth:EmailCodeInvalid")
#endif
                    ;
            }
        }
        else
        {
            var isValidCaptcha = await captchaAppService.ValidateCaptchaAsync(input.CaptchaToken ?? string.Empty, input.CaptchaCode ?? string.Empty, cancellationToken);
            if (!isValidCaptcha)
            {
                throw new BadRequestException("The image captcha is incorrect or has expired.")
#if (IncludeLocalization)
                    .WithCode("Auth:CaptchaInvalid")
#endif
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
        return await GetCurrentUserOutputAsync(userId, cancellationToken);
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
            throw new NotFoundException($"User {userId} not found.")
#if (IncludeLocalization)
                .WithCode("User:NotFound")
                .WithData("Id", userId)
#endif
                ;
        }

        logger.LogInformation("Updating current user profile (ID: {UserId})", user.Id);

        await userDomainService.UpdateProfileAsync(
            user,
            input.Username,
            input.Email,
            input.DisplayName,
            input.PhoneNumber,
            input.Avatar,
            cancellationToken);

        await userRepository.UpdateAsync(user, cancellationToken);
        logger.LogInformation("Current user profile updated (ID: {UserId})", user.Id);

        return await GetCurrentUserOutputAsync(user.Id, cancellationToken);
    }

    /// <summary>
    /// 修改密码
    /// </summary>
    public async Task ChangePasswordAsync(ChangePasswordInputDto input, CancellationToken cancellationToken = default)
    {
        var userId = currentUser.Id!.Value;
        var user = await userRepository.GetByIdAsync(userId, cancellationToken);
        if (user == null)
        {
            throw new NotFoundException($"User {userId} not found.")
#if (IncludeLocalization)
                .WithCode("User:NotFound")
                .WithData("Id", userId)
#endif
                ;
        }

        logger.LogInformation("Changing current user password (ID: {UserId})", user.Id);

        await userDomainService.ChangePasswordAsync(user, input.CurrentPassword, input.NewPassword, cancellationToken);
        await userRepository.UpdateAsync(user, cancellationToken);

        logger.LogInformation("Current user password changed (ID: {UserId})", user.Id);

        // 改自己的密码：此刻主体已建立，操作人字段有值，目标即本人。
        await operationRecorder.RecordSucceededAsync(
            OperationRecordActions.AuthPasswordChanged,
            OperationTarget.For(user.Id, user.DisplayName ?? user.Username),
            OperationRecordAuthorizations.AuthenticatedSelf,
            cancellationToken);
    }

    private async Task<UserOutputDto> GetCurrentUserOutputAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await userRepository.GetByIdAsync(userId, cancellationToken);
        if (user == null)
        {
            throw new NotFoundException($"User {userId} not found.")
#if (IncludeLocalization)
                .WithCode("User:NotFound")
                .WithData("Id", userId)
#endif
                ;
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
