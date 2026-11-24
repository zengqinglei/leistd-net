#if (LocalIdentity)
using CompanyName.ProjectName.Application.OperationRecords.Provider;
using CompanyName.ProjectName.Application.Permissions.Provider;
using CompanyName.ProjectName.Application.Settings.Dtos;
using CompanyName.ProjectName.Application.Settings.Errors;
using Leistd.Ddd.Application.AppServices;
using Leistd.Email.Abstractions;
using Leistd.Email.Exceptions;
using Leistd.ExceptionHandling;
using Leistd.OperationRecords.Models;
using Leistd.OperationRecords.Recording;
using Microsoft.Extensions.Logging;
using Leistd.Redaction;

namespace CompanyName.ProjectName.Application.Settings.AppServices;

/// <inheritdoc cref="IEmailSettingsAppService"/>
public class EmailSettingsAppService(
    IEmailSender emailSender,
    IOperationRecorder operationRecorder,
    ILogger<EmailSettingsAppService> logger) : BaseAppService, IEmailSettingsAppService
{
    /// <inheritdoc />
    public async Task SendTestEmailAsync(SendTestEmailInputDto input, CancellationToken cancellationToken = default)
    {
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
            // 按失败类别给码，界面据此指明该查哪一项；归不了类的仍是通用原因
            var (code, message) = (ex as EmailDeliveryException)?.FailureKind switch
            {
                EmailDeliveryFailureKind.Unreachable => (AppSettingErrorCodes.TestEmailUnreachable,
                    "The email server could not be reached or is temporarily unavailable. Check the host, port and TLS settings, or try again later."),
                EmailDeliveryFailureKind.AuthenticationRejected => (AppSettingErrorCodes.TestEmailAuthRejected,
                    "The email server rejected the sign-in. Check the username and password."),
                EmailDeliveryFailureKind.DeliveryRejected => (AppSettingErrorCodes.TestEmailRejected,
                    "The email server refused the message. Check the sender address and the recipient."),
                _ => (AppSettingErrorCodes.TestEmailFailed,
                    "The test email could not be sent. Check the email settings and server logs."),
            };
            throw new BusinessException(code, message, ex);
        }

        // 不带目标：收件人是任意填写的地址，写进审计表就成了一份可被读取的联系方式清单
        await operationRecorder.RecordSucceededAsync(
            OperationRecordActions.SettingTestEmailSent,
            OperationTarget.None,
            PermissionConstant.Settings.Email,
            cancellationToken);
    }
}
#endif
