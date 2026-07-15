using System.Security.Cryptography;
using CompanyName.ProjectName.Application.Auth.Dtos;
using CompanyName.ProjectName.Domain.Shared.Email;
using CompanyName.ProjectName.Domain.Users.Entities;
using CompanyName.ProjectName.Domain.Users.Options;
using Leistd.Ddd.Application.AppService;
using Leistd.Ddd.Domain.Repositories;
using Leistd.Exception.Core;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;

namespace CompanyName.ProjectName.Application.Auth.AppServices;

public class EmailVerificationAppService(
    IDistributedCache distributedCache,
    IOptions<UserRegistrationOptions> options,
    ICaptchaAppService captchaAppService,
    IEmailSender emailSender,
    IRepository<User, Guid> userRepository) : BaseAppService, IEmailVerificationAppService
{
    private readonly UserRegistrationOptions _options = options.Value;

    public async Task SendEmailCodeAsync(SendEmailCodeInputDto input, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = NormalizeEmail(input.Email);
        var isValidCaptcha = await captchaAppService.ValidateCaptchaAsync(input.CaptchaToken, input.CaptchaCode, cancellationToken);
        if (!isValidCaptcha)
        {
            throw new BadRequestException("The image captcha is incorrect or has expired.")
#if (IncludeLocalization)
                .WithLocalization("Auth:CaptchaInvalid")
#endif
                ;
        }

        var existingUser = await userRepository.GetFirstAsync(u => u.Email.ToLower() == normalizedEmail, cancellationToken: cancellationToken);
        if (existingUser != null)
        {
            throw new BadRequestException("This email address is already in use.")
#if (IncludeLocalization)
                .WithLocalization("Auth:EmailAlreadyUsed")
#endif
                ;
        }

        var limitKey = GetLimitCacheKey(normalizedEmail);
        var isLimited = await distributedCache.GetStringAsync(limitKey, cancellationToken);
        if (!string.IsNullOrEmpty(isLimited))
        {
            throw new BadRequestException("Verification codes are being sent too frequently. Please try again later.")
#if (IncludeLocalization)
                .WithLocalization("Auth:EmailCodeSendTooFrequent")
#endif
                ;
        }

        var code = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
        var subject = "Account Registration Verification Code";
        var htmlBody = $@"
<div style='font-family: Arial, sans-serif; max-w-md: 600px; margin: 0 auto; border: 1px solid #e2e8f0; border-radius: 8px; overflow: hidden;'>
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
        var codeCacheKey = GetCodeCacheKey(normalizedEmail);
        await distributedCache.SetStringAsync(codeCacheKey, code, new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(_options.EmailCodeExpiryMinutes)
        }, cancellationToken);

        try
        {
            await emailSender.SendAsync(normalizedEmail, subject, htmlBody, cancellationToken);
        }
        catch
        {
            await distributedCache.RemoveAsync(codeCacheKey, cancellationToken);
            throw;
        }

        await distributedCache.SetStringAsync(limitKey, "1", new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(_options.EmailCodeSendIntervalSeconds)
        }, cancellationToken);
    }

    public async Task<bool> ValidateEmailCodeAsync(string email, string code, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(code))
            return false;

        var codeCacheKey = GetCodeCacheKey(NormalizeEmail(email));
        var cachedCode = await distributedCache.GetStringAsync(codeCacheKey, cancellationToken);

        if (string.IsNullOrEmpty(cachedCode) || !cachedCode.Equals(code.Trim(), StringComparison.OrdinalIgnoreCase))
            return false;

        await distributedCache.RemoveAsync(codeCacheKey, cancellationToken);
        return true;
    }

    private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();
    private static string GetCodeCacheKey(string email) => $"MyProject:EmailCode:{email}";
    private static string GetLimitCacheKey(string email) => $"MyProject:EmailCodeLimit:{email}";
}
