using CompanyName.ProjectName.Domain.Auth.VerificationCodes;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CompanyName.ProjectName.Application.Auth.Dtos;
using CompanyName.ProjectName.Domain.Shared.Email;
using CompanyName.ProjectName.Domain.Shared.Security.PasswordHash;
using CompanyName.ProjectName.Domain.Users.Entities;
using CompanyName.ProjectName.Domain.Users.Options;
using Leistd.Ddd.Application.AppService;
using Leistd.Ddd.Domain.Repositories;
using Leistd.MultiTenancy;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Leistd.ExceptionHandling;
using Leistd.Lock;
using Leistd.Lock.Abstractions;
using Leistd.MultiTenancy.Abstractions;

namespace CompanyName.ProjectName.Application.Auth.AppServices;

public class EmailVerificationAppService(
    IDistributedCache distributedCache,
    IDistributedLock distributedLock,
    IVerificationCodeDigest codeDigest,
    IOptions<UserRegistrationOptions> options,
    ICaptchaAppService captchaAppService,
    IEmailSender emailSender,
    ILogger<EmailVerificationAppService> logger,
    IRepository<User, Guid> userRepository
    , ICurrentTenant currentTenant
    ) : BaseAppService, IEmailVerificationAppService
{
    private const string RegistrationPurpose = "registration-email";
    private const string CacheKeyPrefix = "MyProject:email-verification";
    private readonly UserRegistrationOptions _options = options.Value;

    public async Task<EmailVerificationChallengeOutputDto> SendEmailCodeAsync(
        SendEmailCodeInputDto input,
        CancellationToken cancellationToken = default)
    {
        // 功能关闭时明确拒绝。不拒绝的话请求会一路走到摘要计算，
        // 在"密钥未配置"处失败——那个错误对调用方毫无意义，
        // 因为它真正的问题是这个功能压根没开
        if (!options.Value.EnableEmailVerification)
        {
            throw new BadRequestException("Email verification is not enabled.")
#if (IncludeLocalization)
                .WithCode("Auth:EmailVerificationDisabled")
#endif
                ;
        }

        var normalizedEmail = NormalizeEmail(input.Email);
        var isValidCaptcha = await captchaAppService.ValidateCaptchaAsync(
            input.CaptchaToken,
            input.CaptchaCode,
            cancellationToken);
        if (!isValidCaptcha)
        {
            throw new BadRequestException("The image captcha is incorrect or has expired.")
#if (IncludeLocalization)
                .WithCode("Auth:CaptchaInvalid")
#endif
                ;
        }

        var existingUser = await userRepository.GetFirstAsync(
            u => u.Email.ToLower() == normalizedEmail,
            cancellationToken: cancellationToken);
        if (existingUser != null)
        {
            throw new BadRequestException("This email address is already in use.")
#if (IncludeLocalization)
                .WithCode("Auth:EmailAlreadyUsed")
#endif
                ;
        }

        var scope = GetScope();
        var emailDigest = GetEmailDigest(normalizedEmail);
        var rateKey = GetRateCacheKey(scope, emailDigest);
        await using var rateLock = await distributedLock.LockAsync(GetRateLockKey(scope, emailDigest), cancellationToken);
        using var lockScope = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, rateLock.LockLost);
        var operationToken = lockScope.Token;

        var isLimited = await distributedCache.GetStringAsync(rateKey, operationToken);
        if (!string.IsNullOrEmpty(isLimited))
        {
            throw new BadRequestException("Verification codes are being sent too frequently. Please try again later.")
#if (IncludeLocalization)
                .WithCode("Auth:EmailCodeSendTooFrequent")
#endif
                ;
        }

        var challengeId = Guid.NewGuid();
        var code = RandomNumberGenerator.GetInt32(100000, 1000000).ToString(CultureInfo.InvariantCulture);
        var expiresIn = TimeSpan.FromMinutes(_options.EmailCodeExpiryMinutes);
        var challenge = new EmailVerificationChallengeState
        {
            Scope = scope,
            Purpose = RegistrationPurpose,
            EmailDigest = emailDigest,
            CodeHash = codeDigest.Compute(code),
            ExpiresAt = DateTimeOffset.UtcNow.Add(expiresIn),
            RemainingAttempts = _options.EmailCodeMaxAttempts
        };
        var challengeKey = GetChallengeCacheKey(challengeId);

        // Reserve the send slot before the external email call so concurrent requests cannot both send.
        await distributedCache.SetStringAsync(rateKey, "1", new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(_options.EmailCodeSendIntervalSeconds)
        }, operationToken);
        try
        {
            await distributedCache.SetStringAsync(
                challengeKey,
                JsonSerializer.Serialize(challenge),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = expiresIn },
                operationToken);
            await emailSender.SendAsync(
                normalizedEmail,
                "Account Registration Verification Code",
                BuildEmailBody(code),
                operationToken);
        }
        catch
        {
            // A failed send must not strand either an unusable challenge or a rate-limit reservation.
            try
            {
                await Task.WhenAll(
                    distributedCache.RemoveAsync(challengeKey, CancellationToken.None),
                    distributedCache.RemoveAsync(rateKey, CancellationToken.None));
            }
            catch (Exception cleanupException)
            {
                logger.LogWarning(
                    cleanupException,
                    "Failed to clean up email verification challenge {ChallengeId} after send failure",
                    challengeId);
            }
            throw;
        }

        return new EmailVerificationChallengeOutputDto
        {
            ChallengeId = challengeId,
            ExpiresInSeconds = checked((int)expiresIn.TotalSeconds),
            RetryAfterSeconds = _options.EmailCodeSendIntervalSeconds
        };
    }

    public async Task<bool> ValidateEmailChallengeAsync(
        string email,
        EmailVerificationInputDto verification,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email) ||
            verification.ChallengeId == Guid.Empty ||
            string.IsNullOrWhiteSpace(verification.Code))
        {
            return false;
        }

        var challengeKey = GetChallengeCacheKey(verification.ChallengeId);
        await using var challengeLock = await distributedLock.LockAsync(
            GetChallengeLockKey(verification.ChallengeId),
            cancellationToken);
        using var lockScope = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            challengeLock.LockLost);
        var operationToken = lockScope.Token;

        var serializedChallenge = await distributedCache.GetStringAsync(challengeKey, operationToken);
        if (string.IsNullOrEmpty(serializedChallenge))
        {
            return false;
        }

        EmailVerificationChallengeState? challenge;
        try
        {
            challenge = JsonSerializer.Deserialize<EmailVerificationChallengeState>(serializedChallenge);
        }
        catch (JsonException)
        {
            await distributedCache.RemoveAsync(challengeKey, operationToken);
            return false;
        }

        if (challenge is null)
        {
            await distributedCache.RemoveAsync(challengeKey, operationToken);
            return false;
        }

        var emailDigest = GetEmailDigest(NormalizeEmail(email));
        if (!string.Equals(challenge.Scope, GetScope(), StringComparison.Ordinal) ||
            !string.Equals(challenge.Purpose, RegistrationPurpose, StringComparison.Ordinal) ||
            !FixedTimeEquals(challenge.EmailDigest, emailDigest))
        {
            // A request from another tenant/email must not be able to consume or exhaust this challenge.
            return false;
        }

        var remainingLifetime = challenge.ExpiresAt - DateTimeOffset.UtcNow;
        if (remainingLifetime <= TimeSpan.Zero)
        {
            await distributedCache.RemoveAsync(challengeKey, operationToken);
            return false;
        }

        bool codeMatches;
        try
        {
            codeMatches = codeDigest.Matches(challenge.CodeHash, verification.Code.Trim());
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException)
        {
            await distributedCache.RemoveAsync(challengeKey, operationToken);
            return false;
        }

        if (codeMatches)
        {
            await distributedCache.RemoveAsync(challengeKey, operationToken);
            return true;
        }

        challenge = challenge with { RemainingAttempts = challenge.RemainingAttempts - 1 };
        if (challenge.RemainingAttempts <= 0)
        {
            await distributedCache.RemoveAsync(challengeKey, operationToken);
            return false;
        }

        await distributedCache.SetStringAsync(
            challengeKey,
            JsonSerializer.Serialize(challenge),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = remainingLifetime },
            operationToken);
        return false;
    }

    private string BuildEmailBody(string code) => $@"
<div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto; border: 1px solid #e2e8f0; border-radius: 8px; overflow: hidden;'>
    <div style='background-color: #0f172a; padding: 20px; text-align: center; color: white;'>
        <h2 style='margin: 0;'>Account Registration Verification Code</h2>
    </div>
    <div style='padding: 30px; background-color: #f8fafc; color: #334155;'>
        <p>Hello,</p>
        <p>You are registering an account. Your verification code is:</p>
        <div style='margin: 20px 0; text-align: center;'>
            <span style='font-size: 32px; font-weight: bold; letter-spacing: 8px; color: #2563eb;'>{code}</span>
        </div>
        <p style='font-size: 14px; color: #64748b;'>This code will expire in {_options.EmailCodeExpiryMinutes} minutes. Please do not share it with anyone.</p>
        <p style='font-size: 14px; color: #64748b; margin-top: 30px;'>If you did not request this, please ignore this email.</p>
    </div>
</div>";

    private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    private static string GetEmailDigest(string normalizedEmail)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedEmail)));

    private string GetScope()
    {
        return currentTenant.Id is { } tenantId ? $"tenant:{tenantId:N}" : "host";
    }

    private static bool FixedTimeEquals(string left, string right)
    {
        var leftBytes = Encoding.ASCII.GetBytes(left);
        var rightBytes = Encoding.ASCII.GetBytes(right);
        return leftBytes.Length == rightBytes.Length &&
               CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }

    private static string GetChallengeCacheKey(Guid challengeId)
        => $"{CacheKeyPrefix}:challenge:{challengeId:N}";

    private static string GetChallengeLockKey(Guid challengeId)
        => $"{CacheKeyPrefix}:lock:challenge:{challengeId:N}";

    private static string GetRateCacheKey(string scope, string emailDigest)
        => $"{CacheKeyPrefix}:rate:{scope}:{emailDigest}";

    private static string GetRateLockKey(string scope, string emailDigest)
        => $"{CacheKeyPrefix}:lock:rate:{scope}:{emailDigest}";

    private sealed record EmailVerificationChallengeState
    {
        public required string Scope { get; init; }

        public required string Purpose { get; init; }

        public required string EmailDigest { get; init; }

        public required string CodeHash { get; init; }

        public required DateTimeOffset ExpiresAt { get; init; }

        public required int RemainingAttempts { get; init; }
    }
}
