using CompanyName.ProjectName.Application.Settings.Provider;
using CompanyName.ProjectName.Application.Settings.Timing;
using Leistd.ExceptionHandling;
using Leistd.Settings.Validation;

namespace CompanyName.ProjectName.Application.Settings.Validators;

/// <summary>
/// 展示时区只接受 IANA 标识，与读取端共用同一套判定，不会出现"写得进去却解析不出来"。
/// </summary>
internal sealed class TimeZoneSettingValidator(IUserTimeZoneProvider userTimeZoneProvider) : ISettingValueValidator
{
    public Task ValidateAsync(SettingValueValidationContext context, CancellationToken cancellationToken = default)
    {
        if (context.Definition.Name == SettingConstant.Display.TimeZone && !userTimeZoneProvider.IsValidId(context.Value))
        {
            throw new BadRequestException($"'{context.Value}' is not a valid IANA time zone id.")
#if (IncludeLocalization)
                .WithCode("Setting:TimeZoneInvalid")
                .WithData("Value", context.Value)
#endif
                ;
        }

        return Task.CompletedTask;
    }
}
