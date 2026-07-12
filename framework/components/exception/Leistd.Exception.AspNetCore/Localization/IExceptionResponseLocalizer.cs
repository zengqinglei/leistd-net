using Leistd.Exception.Core;

namespace Leistd.Exception.AspNetCore.Localization;

/// <summary>
/// Localizes the user-facing parts of an exception response at the HTTP boundary.
/// </summary>
public interface IExceptionResponseLocalizer
{
    /// <summary>
    /// Returns localized values for the current request culture. Null values use framework fallbacks.
    /// </summary>
    ExceptionResponseLocalization Localize(BusinessException exception, int statusCode);
}
