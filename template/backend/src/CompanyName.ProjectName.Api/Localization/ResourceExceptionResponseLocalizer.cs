using Leistd.Exception.AspNetCore.Localization;
using Leistd.Exception.Core;
using Microsoft.Extensions.Localization;

namespace CompanyName.ProjectName.Api.Localization;

public sealed class ResourceExceptionResponseLocalizer(IStringLocalizer<ApiResource> localizer)
    : IExceptionResponseLocalizer
{
    public ExceptionResponseLocalization Localize(BusinessException exception, int statusCode)
    {
        var messageKey = exception.LocalizationKey ?? $"Exception:{exception.Code}";
        var arguments = exception.LocalizationArguments.Select(argument => argument ?? string.Empty).ToArray();
        var message = localizer[messageKey, arguments];
        var title = localizer[$"ProblemDetails.Title:{statusCode}"];
        var errors = exception is UnprocessableEntityException validationException
            ? LocalizeValidationErrors(validationException.ValidationErrors)
            : null;

        return new ExceptionResponseLocalization(
            message.ResourceNotFound ? null : message.Value,
            title.ResourceNotFound ? null : title.Value,
            errors);
    }

    private Dictionary<string, string[]>? LocalizeValidationErrors(Dictionary<string, string[]>? errors)
    {
        return errors?.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.Select(error =>
            {
                var localized = localizer[error];
                return localized.ResourceNotFound ? error : localized.Value;
            }).ToArray());
    }
}
