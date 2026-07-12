namespace Leistd.Exception.AspNetCore.Localization;

/// <summary>
/// Localized, user-facing values for a ProblemDetails response.
/// </summary>
public sealed record ExceptionResponseLocalization(
    string? Message = null,
    string? Title = null,
    IDictionary<string, string[]>? Errors = null);
