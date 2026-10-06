#if (LocalIdentity)
using CompanyName.ProjectName.Application.OperationRecords.Provider;
using CompanyName.ProjectName.Application.Permissions.Provider;
using CompanyName.ProjectName.Application.Settings.Dtos;
using CompanyName.ProjectName.Application.Settings.Errors;
using Leistd.Ddd.Application.AppServices;
using Leistd.Email.Abstractions;
using Leistd.ExceptionHandling;
using Leistd.MultiTenancy.Context;
using Leistd.OperationRecords.Models;
using Leistd.OperationRecords.Recording;
using Microsoft.Extensions.Logging;
using Leistd.Redaction;

namespace CompanyName.ProjectName.Application.Settings.AppServices;

/// <inheritdoc cref="IEmailSettingsAppService"/>
public class EmailSettingsAppService(
    ICurrentTenant currentTenant,
    IEmailSender emailSender,
    IOperationRecorder operationRecorder,
    ILogger<EmailSettingsAppService> logger) : BaseAppService, IEmailSettingsAppService
{
    /// <inheritdoc />
    public async Task SendTestEmailAsync(SendTestEmailInputDto input, CancellationToken cancellationToken = default)
    {
        // 发信参数是进程级的，只有宿主能改，也只有宿主来试
        if (currentTenant.Id is not null)
            throw new BusinessException(AppSettingErrorCodes.TestEmailHostOnly, "The email settings can only be tested on the host.");

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
            logger.LogWarning(ex, "Test email to {To} failed", TextRedactor.RedactEmail(input.To));
            throw new BusinessException(AppSettingErrorCodes.TestEmailFailed, "The test email could not be sent. Check the email settings and server logs.", ex);
        }

        // 不带目标：收件人是任意填写的地址，写进审计表就成了一份可被读取的联系方式清单
        await operationRecorder.RecordSucceededAsync(
            OperationRecordActions.SettingTestEmailSent,
            OperationTarget.None,
            PermissionConstant.Settings.Default,
            cancellationToken);
    }
}
#endif
