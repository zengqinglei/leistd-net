using Leistd.UnitOfWork.Attributes;
using CompanyName.ProjectName.Application.Users.Mappings;
using Leistd.ObjectMapping.Abstractions;
using CompanyName.ProjectName.Application.Auth.Dtos;
using CompanyName.ProjectName.Domain.Users.DomainServices;
using CompanyName.ProjectName.Domain.Users.Entities;
using Leistd.Ddd.Application.AppService;
using Leistd.Ddd.Domain.Repositories;
using Leistd.Security.Users;
using Microsoft.Extensions.Logging;

using CompanyName.ProjectName.Domain.Users.Options;
using Microsoft.Extensions.Options;
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
    IObjectMapper objectMapper,
    IOptions<UserRegistrationOptions> securityOptions,
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
            throw new UnauthorizedException($"Login failed: user not found or incorrect password - {input.UsernameOrEmail}")
#if (IncludeLocalization)
                .WithCode("Auth:InvalidCredentials")
                .WithData("UsernameOrEmail", input.UsernameOrEmail)
#endif
                ;
        }

        return await sessionSignInService.SignInAsync(user, cancellationToken: cancellationToken);
    }

    /// <remarks>建用户与分配默认角色必须同生共死：拆开后注册失败会留下没有任何角色的用户。</remarks>
    [UnitOfWork]
    public async Task<UserOutputDto> RegisterAsync(RegisterInputDto input, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Registering user {Username} with email {Email}", input.Username, input.Email);

        var options = securityOptions.Value;

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
