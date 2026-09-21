#if (LocalIdentity)
using CompanyName.ProjectName.Application.Auth.Dtos;
using CompanyName.ProjectName.Application.Auth.Policies;
using CompanyName.ProjectName.Application.Auth.SecurityAlerts;
using CompanyName.ProjectName.Application.Auth.Sessions;
using CompanyName.ProjectName.Domain.Auth.DomainServices;
using CompanyName.ProjectName.Application.OperationRecords.Provider;
using CompanyName.ProjectName.Domain.Shared.Security.PasswordHash;
using CompanyName.ProjectName.Domain.Users.Entities;
using CompanyName.ProjectName.Domain.Users.DomainServices;
using CompanyName.ProjectName.Domain.Shared.Security.OneTimeCodes;
using CompanyName.ProjectName.Domain.Shared.Text;
using Leistd.Ddd.Application.AppServices;
using Leistd.Ddd.Domain.Repositories;
using Leistd.ExceptionHandling;
using Leistd.MultiTenancy.ConnectionStrings;
using Leistd.MultiTenancy.Context;
using Leistd.MultiTenancy.Errors;
using Leistd.MultiTenancy.Management;
using Leistd.MultiTenancy.Tenancy;
using Leistd.OperationRecords.Definitions;
using Leistd.OperationRecords.Models;
using Leistd.OperationRecords.Queries;
using Leistd.OperationRecords.Recording;
using Leistd.OperationRecords.Stores;
using Leistd.Security.Users;
using Leistd.Timing;
using Microsoft.Extensions.Caching.Distributed;

namespace CompanyName.ProjectName.Application.Auth.AppServices;

/// <inheritdoc cref="ITwoFactorAppService" />
/// <remarks>
/// 待启用的密钥放在缓存里而不是用户行上：没确认的设置不该改变账号状态，
/// 半途放弃也不留痕。缓存里存的同样是加密后的密钥。
/// </remarks>
internal sealed class TwoFactorAppService(
    IRepository<User, Guid> userRepository,
    ICurrentUser currentUser,
    ICurrentTenant currentTenant,
    TwoFactorDomainService twoFactorDomainService,
    IPasswordHasher passwordHasher,
    UserSessionDomainService userSessionDomainService,
    ILoginSecurityPolicyProvider loginSecurityPolicy,
    IOperationRecorder operationRecorder,
    ISecurityAlertPublisher securityAlerts,
    IDistributedCache cache,
    IClock clock) : BaseAppService, ITwoFactorAppService
{
    // 身份验证器应用里条目的发行方，生成项目后即项目名
    private const string Issuer = "CompanyName.ProjectName";
    private const string SetupKeyPrefix = "auth:2fa-setup:";
    private static readonly TimeSpan SetupLifetime = TimeSpan.FromMinutes(10);

    /// <inheritdoc />
    public async Task<TwoFactorStatusOutputDto> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var user = await GetCurrentUserEntityAsync(cancellationToken);
        var policy = await loginSecurityPolicy.GetAsync(cancellationToken);
        return new TwoFactorStatusOutputDto
        {
            Enabled = user.TwoFactorEnabled,
            RecoveryCodesLeft = user.TwoFactorEnabled ? user.RecoveryCodesLeft : 0,
            RequiredByPolicy = policy.RequireTwoFactor
        };
    }

    /// <inheritdoc />
    public async Task<TwoFactorSetupOutputDto> BeginSetupAsync(CancellationToken cancellationToken = default)
    {
        var user = await GetCurrentUserEntityAsync(cancellationToken);
        EnsureDisabled(user);

        var secret = Totp.GenerateSecret();
        await cache.SetStringAsync(
            SetupKey(user.Id),
            twoFactorDomainService.ProtectSecret(secret),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = SetupLifetime },
            cancellationToken);

        var base32 = Base32.Encode(secret);
        // 同名账号在不同租户各有一个，账号名带上租户，应用里才分得清
        var account = currentTenant.Name is { Length: > 0 } tenantName
            ? $"{user.Username}@{tenantName}"
            : user.Username;
        return new TwoFactorSetupOutputDto
        {
            Secret = base32,
            OtpAuthUri = Totp.BuildUri(Issuer, account, base32)
        };
    }

    /// <inheritdoc />
    public async Task<TwoFactorRecoveryCodesOutputDto> EnableAsync(
        TwoFactorCodeInputDto input,
        CancellationToken cancellationToken = default)
    {
        var user = await GetCurrentUserEntityAsync(cancellationToken);
        EnsureDisabled(user);

        var protectedSecret = await cache.GetStringAsync(SetupKey(user.Id), cancellationToken)
            ?? throw new BadRequestException("The setup has expired. Start again.")
#if (IncludeLocalization)
                .WithCode("Auth:TwoFactorSetupExpired")
#endif
                ;

        if (twoFactorDomainService.VerifySetupCode(protectedSecret, input.Code, clock.Now) is not { } step)
        {
            throw CodeInvalid();
        }

        var codes = RecoveryCodes.Generate();
        user.EnableTwoFactor(protectedSecret, codes.Select(RecoveryCodes.Hash), step);
        await userRepository.UpdateAsync(user, cancellationToken);
        await cache.RemoveAsync(SetupKey(user.Id), cancellationToken);

        // 登录方式变了，此前的其他会话都是在没有第二道门时建立的
        await userSessionDomainService.RevokeAllAsync(user.Id, currentUser.GetSessionId(), cancellationToken);

        await operationRecorder.RecordSucceededAsync(
            OperationRecordActions.AuthTwoFactorEnabled,
            OperationTarget.For(user.Id, user.DisplayName ?? user.Username),
            OperationRecordAuthorizations.AuthenticatedSelf,
            cancellationToken);
        await securityAlerts.PublishAsync(user.Id, new SecurityAlert(SecurityAlertKind.TwoFactorEnabled), cancellationToken);

        return new TwoFactorRecoveryCodesOutputDto { RecoveryCodes = codes };
    }

    /// <inheritdoc />
    public async Task DisableAsync(DisableTwoFactorInputDto input, CancellationToken cancellationToken = default)
    {
        var user = await GetCurrentUserEntityAsync(cancellationToken);
        if (!user.TwoFactorEnabled)
            return;

        if ((await loginSecurityPolicy.GetAsync(cancellationToken)).RequireTwoFactor)
        {
            throw new BadRequestException("Two-factor authentication is required here and cannot be turned off.")
#if (IncludeLocalization)
                .WithCode("Auth:TwoFactorRequiredByPolicy")
#endif
                ;
        }

        if (user.PasswordHash is null || !passwordHasher.VerifyPassword(user.PasswordHash, input.Password))
        {
            throw new BadRequestException("The current password is incorrect.")
#if (IncludeLocalization)
                .WithCode("Security:CurrentPasswordIncorrect")
#endif
                ;
        }

        if (!twoFactorDomainService.VerifyCode(user, input.Code, clock.Now))
        {
            throw CodeInvalid();
        }

        user.DisableTwoFactor();
        await userRepository.UpdateAsync(user, cancellationToken);
        await userSessionDomainService.RevokeAllAsync(user.Id, currentUser.GetSessionId(), cancellationToken);

        await operationRecorder.RecordSucceededAsync(
            OperationRecordActions.AuthTwoFactorDisabled,
            OperationTarget.For(user.Id, user.DisplayName ?? user.Username),
            OperationRecordAuthorizations.AuthenticatedSelf,
            cancellationToken);
        await securityAlerts.PublishAsync(user.Id, new SecurityAlert(SecurityAlertKind.TwoFactorDisabled), cancellationToken);
    }

    /// <inheritdoc />
    public async Task<TwoFactorRecoveryCodesOutputDto> RegenerateRecoveryCodesAsync(
        TwoFactorCodeInputDto input,
        CancellationToken cancellationToken = default)
    {
        var user = await GetCurrentUserEntityAsync(cancellationToken);
        if (!user.TwoFactorEnabled)
        {
            throw new BadRequestException("Two-factor authentication is not turned on.")
#if (IncludeLocalization)
                .WithCode("Auth:TwoFactorNotEnabled")
#endif
                ;
        }

        if (!twoFactorDomainService.VerifyCode(user, input.Code, clock.Now))
        {
            throw CodeInvalid();
        }

        var codes = RecoveryCodes.Generate();
        user.ReplaceRecoveryCodes(codes.Select(RecoveryCodes.Hash));
        await userRepository.UpdateAsync(user, cancellationToken);

        await operationRecorder.RecordSucceededAsync(
            OperationRecordActions.AuthTwoFactorRecoveryCodesRegenerated,
            OperationTarget.For(user.Id, user.DisplayName ?? user.Username),
            OperationRecordAuthorizations.AuthenticatedSelf,
            cancellationToken);

        return new TwoFactorRecoveryCodesOutputDto { RecoveryCodes = codes };
    }

    private static void EnsureDisabled(User user)
    {
        if (user.TwoFactorEnabled)
        {
            throw new BadRequestException("Two-factor authentication is already turned on.")
#if (IncludeLocalization)
                .WithCode("Auth:TwoFactorAlreadyEnabled")
#endif
                ;
        }
    }

    private static BusinessException CodeInvalid() =>
        new BadRequestException("The verification code is incorrect.")
            // 错误码在不含本地化的形态下也要带：界面按它区分"重输"与"回到密码那一步"
            .WithCode("Auth:TwoFactorCodeInvalid");

    private static string SetupKey(Guid userId) => SetupKeyPrefix + userId.ToString("N");

    private async Task<User> GetCurrentUserEntityAsync(CancellationToken cancellationToken)
    {
        var userId = currentUser.Id!.Value;
        return await userRepository.GetByIdAsync(userId, cancellationToken)
            ?? throw new NotFoundException($"User {userId} not found.")
#if (IncludeLocalization)
                .WithCode("User:NotFound")
                .WithData("Id", userId)
#endif
                ;
    }
}
#endif
