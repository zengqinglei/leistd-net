#if (LocalIdentity)
using CompanyName.ProjectName.Application.Permissions.Provider;
using CompanyName.ProjectName.Application.Settings.Dtos;
using Leistd.Authorization.Checking;
using Leistd.Authorization.Definitions;
using Leistd.Authorization.Errors;
using Leistd.Authorization.Grants;
using Leistd.Authorization.Management;
using Leistd.Authorization.Subjects;
using Leistd.Ddd.Application.AppServices;
using Leistd.Email.Abstractions;
using Leistd.ExceptionHandling;
using Leistd.MultiTenancy.ConnectionStrings;
using Leistd.MultiTenancy.Context;
using Leistd.MultiTenancy.Errors;
using Leistd.MultiTenancy.Management;
using Leistd.MultiTenancy.Tenancy;
using Microsoft.Extensions.Logging;

namespace CompanyName.ProjectName.Application.Settings.AppServices;

/// <inheritdoc cref="IEmailSettingsAppService"/>
public class EmailSettingsAppService(
    IPermissionChecker permissionChecker,
    ICurrentTenant currentTenant,
    IEmailSender emailSender,
    ILogger<EmailSettingsAppService> logger) : BaseAppService, IEmailSettingsAppService
{
    /// <inheritdoc />
    public async Task SendTestEmailAsync(SendTestEmailInputDto input, CancellationToken cancellationToken = default)
    {
        if (!await permissionChecker.IsGrantedAsync(PermissionConstant.Settings.Default, cancellationToken))
            throw new ForbiddenException("Sending a test email requires the settings management permission.")
#if (IncludeLocalization)
                .WithCode("Setting:ManagePermissionRequired")
#endif
                ;

        // 发信参数是进程级的，只有宿主能改，也只有宿主来试
        if (currentTenant.Id is not null)
            throw new ForbiddenException("The email settings can only be tested on the host.")
#if (IncludeLocalization)
                .WithCode("Setting:TestEmailHostOnly")
#endif
                ;

        try
        {
            await emailSender.SendAsync(
                new EmailMessage
                {
                    To = input.To,
                    Subject = "Test email",
                    Body = "<p>This is a test email. If you can read it, the email settings work.</p>"
                },
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // 失败原因原样回给管理员：连不上、认证失败、发件地址被拒，都要靠这句话来改参数
            logger.LogWarning(ex, "Test email to {To} failed", input.To);
            throw new BadRequestException($"The test email could not be sent: {ex.Message}")
#if (IncludeLocalization)
                .WithCode("Setting:TestEmailFailed")
                .WithData("Reason", ex.Message)
#endif
                ;
        }
    }
}
#endif
