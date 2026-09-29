using Microsoft.Extensions.Options;
using Leistd.Notifications.Email.Channels;

namespace Leistd.Notifications.Email.Options;

// 报错里的键名按实际绑定的配置节给出，宿主改了节路径时照提示去改才对得上
internal sealed class EmailNotificationOptionsValidator(string sectionPath = EmailNotificationOptions.SectionName)
    : IValidateOptions<EmailNotificationOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, EmailNotificationOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.PublicBaseUrl) || EmailNotificationLinks.IsHttpUrl(options.PublicBaseUrl))
        {
            return ValidateOptionsResult.Success;
        }

        return ValidateOptionsResult.Fail(
            $"{sectionPath}:PublicBaseUrl '{options.PublicBaseUrl}' must be an absolute http(s) URL.");
    }
}
