#if (LocalIdentity)
using CompanyName.ProjectName.Application.Settings.Provider;
using CompanyName.ProjectName.Domain.Auth.Options;
using Leistd.ExceptionHandling;
using Leistd.Settings.Validation;
using Microsoft.Extensions.Options;

namespace CompanyName.ProjectName.Application.Settings.Validators;

/// <summary>
/// 邮件相关设置的业务取值：开启邮箱验证的运行前提、发件地址的写法。
/// </summary>
internal sealed class EmailSettingValidator(IOptions<VerificationCodeOptions> verificationCodeOptions) : ISettingValueValidator
{
    public Task ValidateAsync(SettingValueValidationContext context, CancellationToken cancellationToken = default)
    {
        switch (context.Definition.Name)
        {
            // 开启前必须确认部署已经给了可用的摘要密钥。密钥只由配置/密钥设施提供、不进设置表；
            // 启动校验只看配置里的布尔值，所以默认部署（关闭邮箱验证、未配密钥）能正常启动——
            // 不在这里拦，管理员随后在设置页打开，直到真的发码时才在摘要计算处抛 500。
            case SettingConstant.Registration.EnableEmailVerification
                when context.Value == "true" && !verificationCodeOptions.Value.IsKeyUsable:
                throw new BadRequestException(
                        "Email verification cannot be enabled: this deployment has no usable "
                        + $"{VerificationCodeOptions.SectionName}:Key. Provide a stable Base64 key of at "
                        + $"least {VerificationCodeOptions.MinimumKeyBytes} bytes from the deployment and restart.")
#if (IncludeLocalization)
                    .WithCode("Setting:EmailVerificationKeyMissing")
                    .WithData("Section", VerificationCodeOptions.SectionName)
                    .WithData("MinimumKeyBytes", VerificationCodeOptions.MinimumKeyBytes)
#endif
                    ;

            // 只收裸地址：带显示名的写法（"Acme <a@b.c>"）在这里放行，发信时才因为解析不出而失败
            case SettingConstant.Email.DefaultFromAddress
                when !System.Net.Mail.MailAddress.TryCreate(context.Value, out var address) || address.Address != context.Value:
                throw new BadRequestException($"'{context.Value}' is not a valid email address.")
#if (IncludeLocalization)
                    .WithCode("Setting:EmailAddressInvalid")
                    .WithData("Value", context.Value)
#endif
                    ;
        }

        return Task.CompletedTask;
    }
}
#endif
