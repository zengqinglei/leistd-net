using CompanyName.ProjectName.Api.Hosting;
using CompanyName.ProjectName.Application.Roles.Errors;
using CompanyName.ProjectName.Application.Settings.Errors;
using CompanyName.ProjectName.Application.Shared.Paging.Errors;
using CompanyName.ProjectName.Domain.Shared.Security.Errors;
using CompanyName.ProjectName.Domain.Users.Errors;
#if (LocalIdentity)
using CompanyName.ProjectName.Application.Tenants.Errors;
using CompanyName.ProjectName.Application.Auth.Errors;
#endif
#if (ExternalLogin)
using CompanyName.ProjectName.Domain.Auth.Errors;
#endif
#if (OpenIddictServer)
using CompanyName.ProjectName.Application.OpenApplications.Errors;
#endif
using Leistd.Authorization.Errors;
using Leistd.ExceptionHandling.AspNetCore.Options;
using Leistd.MultiTenancy.Errors;
using Leistd.Settings.Errors;
#if (IncludeNotifications)
using Leistd.Notifications.Errors;
#endif
using Microsoft.AspNetCore.Http;
#if (ServiceUserContextEnabled)
using Leistd.ExceptionHandling.AspNetCore;
using Leistd.ServiceClient.Exceptions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
#endif
using Xunit;

namespace CompanyName.ProjectName.IntegrationTests;

/// <summary>Locks the host's business-code-to-HTTP contract without starting a web host.</summary>
public sealed class ApiExceptionMappingsTests
{
    [Theory]
#if (LocalIdentity)
    [InlineData(AuthErrorCodes.InvalidCredentials, StatusCodes.Status401Unauthorized)]
    [InlineData(AuthErrorCodes.TwoFactorRequiredByPolicy, StatusCodes.Status403Forbidden)]
#endif
    [InlineData(SettingErrorCodes.IdentityCannotOperate, StatusCodes.Status403Forbidden)]
#if (IncludeNotifications)
    [InlineData(NotificationErrorCodes.IdentityCannotOperate, StatusCodes.Status403Forbidden)]
#endif
    [InlineData(PermissionErrorCodes.SubjectNotFound, StatusCodes.Status404NotFound)]
    [InlineData(MultiTenancyErrorCodes.NotFound, StatusCodes.Status404NotFound)]
#if (LocalIdentity)
    [InlineData(AuthErrorCodes.CannotRevokeCurrentSession, StatusCodes.Status409Conflict)]
    [InlineData(AuthErrorCodes.EmailAlreadyUsed, StatusCodes.Status409Conflict)]
#endif
    [InlineData(UserErrorCodes.EmailAlreadyUsed, StatusCodes.Status409Conflict)]
    [InlineData(MultiTenancyErrorCodes.ConnectionChangeRequiresInactiveTenant, StatusCodes.Status409Conflict)]
    [InlineData(AppSettingErrorCodes.EmailVerificationKeyMissing, StatusCodes.Status409Conflict)]
#if (LocalIdentity)
    [InlineData(AuthErrorCodes.EmailCodeSendTooFrequent, StatusCodes.Status429TooManyRequests)]
    [InlineData(AuthErrorCodes.EmailVerificationUnavailable, StatusCodes.Status503ServiceUnavailable)]
#endif
    [InlineData(AppSettingErrorCodes.TestEmailFailed, StatusCodes.Status503ServiceUnavailable)]
    public void Stable_error_codes_keep_their_declared_http_semantics(string code, int expectedStatusCode)
    {
        var options = new GlobalExceptionOptions();

        ApiExceptionMappings.Configure(options);

        Assert.Equal(expectedStatusCode, options.CodeStatusMappings[code]);
    }

    [Fact]
    public void Explicit_mappings_only_cover_real_codes_with_non_default_statuses()
    {
        var options = new GlobalExceptionOptions();
        ApiExceptionMappings.Configure(options);

        var codeTypes = new[]
        {
            typeof(PagingErrorCodes), typeof(RoleErrorCodes), typeof(SecurityErrorCodes),
            typeof(AppSettingErrorCodes), typeof(UserErrorCodes),
#if (LocalIdentity)
            typeof(AuthErrorCodes), typeof(TenantErrorCodes),
#endif
#if (ExternalLogin)
            typeof(ExternalAuthErrorCodes),
#endif
#if (OpenIddictServer)
            typeof(OpenAppErrorCodes),
#endif
            typeof(PermissionErrorCodes), typeof(MultiTenancyErrorCodes), typeof(SettingErrorCodes),
#if (IncludeNotifications)
            typeof(NotificationErrorCodes),
#endif
        };
        var knownCodes = codeTypes
            .SelectMany(type => type.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToHashSet(StringComparer.Ordinal);

        Assert.All(options.CodeStatusMappings, mapping =>
        {
            Assert.Contains(mapping.Key, knownCodes);
            Assert.NotEqual(StatusCodes.Status400BadRequest, mapping.Value);
        });
    }

#if (ServiceUserContextEnabled)
    [Theory]
    [InlineData(ServiceClientFailureKind.Configuration, 500)]
    [InlineData(ServiceClientFailureKind.Unknown, 500)]
    [InlineData(ServiceClientFailureKind.InvalidResponse, 502)]
    [InlineData(ServiceClientFailureKind.RemoteFailure, 502)]
    [InlineData(ServiceClientFailureKind.Unavailable, 503)]
    [InlineData(ServiceClientFailureKind.Timeout, 504)]
    public async Task Service_client_failures_keep_diagnostics_private(
        ServiceClientFailureKind failureKind, int expectedStatus)
    {
        using var host = await new HostBuilder()
            .ConfigureWebHost(web => web.UseTestServer()
                .ConfigureServices(services => services.AddGlobalExceptionHandler(ApiExceptionMappings.Configure))
                .Configure(app =>
                {
                    app.UseGlobalExceptionHandler();
                    app.Run(_ => throw new ServiceClientException(
                        "private upstream URL and response body", failureKind: failureKind));
                }))
            .StartAsync();

        var response = await host.GetTestClient().GetAsync("/");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(expectedStatus, (int)response.StatusCode);
        // 上游故障只有状态码语义：不合成错误码
        Assert.DoesNotContain("\"code\"", body);
        Assert.DoesNotContain("private upstream URL", body);
    }

    [Theory]
    [InlineData(408)]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(503)]
    [InlineData(504)]
    public async Task Remote_http_failures_use_safe_component_default_without_exposing_remote_details(int remoteStatus)
    {
        using var host = await new HostBuilder()
            .ConfigureWebHost(web => web.UseTestServer()
                .ConfigureServices(services => services.AddGlobalExceptionHandler(ApiExceptionMappings.Configure))
                .Configure(app =>
                {
                    app.UseGlobalExceptionHandler();
                    app.Run(_ => throw new RemoteServiceException(
                        "private upstream URL and response body", remoteStatus,
                        errorCode: "Remote:Private", responseBody: "private payload"));
                }))
            .StartAsync();

        var response = await host.GetTestClient().GetAsync("/");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(StatusCodes.Status502BadGateway, (int)response.StatusCode);
        Assert.DoesNotContain("\"code\"", body);
        Assert.DoesNotContain("private upstream URL", body);
        Assert.DoesNotContain("Remote:Private", body);
        Assert.DoesNotContain("private payload", body);
    }
#endif
}
