using CompanyName.ProjectName.Application.Auth.Dtos;
using CompanyName.ProjectName.Domain.Users.DomainServices;
using CompanyName.ProjectName.Domain.Users.Entities;
using Leistd.Ddd.Application.AppService;
using Leistd.Ddd.Domain.Repositories;
using Leistd.Exception.Core;
using Leistd.Security.Users;
using Microsoft.Extensions.Logging;

using CompanyName.ProjectName.Domain.Users.Options;
using Microsoft.Extensions.Options;

namespace CompanyName.ProjectName.Application.Auth.AppServices;

public class AuthAppService(
    IRepository<User, Guid> userRepository,
    UserDomainService userDomainService,
    ICurrentUser currentUser,
    ICaptchaAppService captchaAppService,
    IEmailVerificationAppService emailVerificationAppService,
    IOptions<UserRegistrationOptions> securityOptions,
    ILogger<AuthAppService> logger) : BaseAppService(), IAuthAppService
{

    public async Task<UserOutputDto> RegisterAsync(RegisterInputDto input, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("开始注册用户 {Username}... 邮箱：{Email}", input.Username, input.Email);

        var options = securityOptions.Value;

        if (options.EnableEmailVerification)
        {
            if (string.IsNullOrWhiteSpace(input.EmailVerificationCode))
            {
                throw new BadRequestException("Please enter the email verification code.")
#if (IncludeLocalization)
                    .WithLocalization("Auth:EmailCodeRequired")
#endif
                    ;
            }

            var isValidEmailCode = await emailVerificationAppService.ValidateEmailCodeAsync(input.Email, input.EmailVerificationCode, cancellationToken);
            if (!isValidEmailCode)
            {
                throw new BadRequestException("The email verification code is incorrect or has expired.")
#if (IncludeLocalization)
                    .WithLocalization("Auth:EmailCodeInvalid")
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
                    .WithLocalization("Auth:CaptchaInvalid")
#endif
                    ;
            }
        }

        var user = await userDomainService.CreateUserAsync(input.Username, input.Email, input.Password, input.DisplayName, cancellationToken);
#if (IncludeRoles)
        await userDomainService.AssignDefaultRolesToUserAsync(user.Id, cancellationToken);
#endif

        logger.LogInformation("用户注册成功 (ID: {Id})", user.Id);

        return await GetCurrentUserOutputAsync(user.Id, cancellationToken);
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
                .WithLocalization("User:NotFound")
                .WithData("Id", userId)
#endif
                ;
        }

        logger.LogInformation("开始更新当前用户资料 (ID: {UserId})", user.Id);

        await userDomainService.UpdateProfileAsync(
            user,
            input.Username,
            input.Email,
            input.DisplayName,
            input.PhoneNumber,
            input.Avatar,
            cancellationToken);

        await userRepository.UpdateAsync(user, cancellationToken);
        logger.LogInformation("更新当前用户资料成功 (ID: {UserId})", user.Id);

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
                .WithLocalization("User:NotFound")
                .WithData("Id", userId)
#endif
                ;
        }

        logger.LogInformation("开始修改当前用户密码 (ID: {UserId})", user.Id);

        await userDomainService.ChangePasswordAsync(user, input.CurrentPassword, input.NewPassword, cancellationToken);
        await userRepository.UpdateAsync(user, cancellationToken);

        logger.LogInformation("修改当前用户密码成功 (ID: {UserId})", user.Id);
    }

    private async Task<UserOutputDto> GetCurrentUserOutputAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await userRepository.GetByIdAsync(userId, cancellationToken);
        if (user == null)
        {
            throw new NotFoundException($"User {userId} not found.")
#if (IncludeLocalization)
                .WithLocalization("User:NotFound")
                .WithData("Id", userId)
#endif
                ;
        }

#if (IncludeRoles)
        var roleNames = await userDomainService.GetUserRoleNamesAsync(userId, cancellationToken);
#endif

        return new UserOutputDto
        {
            Id = user.Id,
            Username = user.Username,
            Email = user.Email,
            DisplayName = user.DisplayName,
            Avatar = user.Avatar,
            PhoneNumber = user.PhoneNumber,
            IsActive = user.IsActive,
            IsSuperAdmin = user.IsSuperAdmin,
            CreationTime = user.CreationTime,
#if (IncludeRoles)
            Roles = [.. roleNames]
#endif
        };
    }
}


