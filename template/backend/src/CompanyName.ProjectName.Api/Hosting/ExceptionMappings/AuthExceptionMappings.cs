#if (LocalIdentity)
using CompanyName.ProjectName.Application.Auth.Errors;
#if (ExternalLogin)
using CompanyName.ProjectName.Domain.Auth.Errors;
#endif
using Leistd.ExceptionHandling.AspNetCore.Options;
using Microsoft.AspNetCore.Http;

namespace CompanyName.ProjectName.Api.Hosting.ExceptionMappings;

internal static class AuthExceptionMappings
{
    public static void Configure(GlobalExceptionOptions options)
    {
        ApiExceptionMappings.Map(options, StatusCodes.Status401Unauthorized,
            AuthErrorCodes.InvalidCredentials,
            AuthErrorCodes.TwoFactorChallengeExpired,
            AuthErrorCodes.UserDisabled,
            AuthErrorCodes.UserLockedOut,
            AuthErrorCodes.UserTemporarilyLockedOut);
        ApiExceptionMappings.Map(options, StatusCodes.Status403Forbidden,
            AuthErrorCodes.TwoFactorSetupRequired,
            AuthErrorCodes.TwoFactorRequiredByPolicy);
        ApiExceptionMappings.Map(options, StatusCodes.Status409Conflict,
            AuthErrorCodes.CannotRevokeCurrentSession,
            AuthErrorCodes.EmailAlreadyVerified,
            AuthErrorCodes.TwoFactorAlreadyEnabled,
            AuthErrorCodes.TwoFactorNotEnabled,
            AuthErrorCodes.EmailAlreadyUsed);
        options.MapCode(AuthErrorCodes.EmailVerificationUnavailable, StatusCodes.Status503ServiceUnavailable);
        options.MapCode(AuthErrorCodes.EmailCodeSendTooFrequent, StatusCodes.Status429TooManyRequests);
#if (ExternalLogin)
        ApiExceptionMappings.Map(options, StatusCodes.Status409Conflict,
            ExternalAuthErrorCodes.AlreadyLinked,
            ExternalAuthErrorCodes.LastSignInMethod,
            ExternalAuthErrorCodes.ProviderAlreadyLinked);
        options.MapCode(ExternalAuthErrorCodes.ProviderNotConfigured, StatusCodes.Status503ServiceUnavailable);
#endif
    }
}
#endif
