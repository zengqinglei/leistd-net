using CompanyName.ProjectName.Application.Settings.Errors;
using CompanyName.ProjectName.Application.Settings.Provider;
using Leistd.ExceptionHandling;
using Leistd.Settings.Validation;

namespace CompanyName.ProjectName.Application.Settings.Validators;

/// <summary>遥测导出设置的写法：基地址必须是绝对 http/https 地址，认证头按 <c>OTEL_EXPORTER_OTLP_HEADERS</c> 的格式。</summary>
/// <remarks>只在写入入口校验一次，发送层只解析不再校验。认证头是机密值，错误里不回显。</remarks>
internal sealed class TelemetryExportSettingValidator : ISettingValueValidator
{
    public Task ValidateAsync(SettingValueValidationContext context, CancellationToken cancellationToken = default)
    {
        switch (context.Definition.Name)
        {
            case SettingConstant.Telemetry.ExportEndpoint when !IsHttpBaseAddress(context.Value):
                throw new BusinessException(AppSettingErrorCodes.TelemetryEndpointInvalid,
                        $"'{context.Value}' is not an absolute http or https address.")
                    .WithData("Value", context.Value);

            case SettingConstant.Telemetry.ExportHeaders when !IsHeaderList(context.Value):
                throw new BusinessException(AppSettingErrorCodes.TelemetryHeadersInvalid,
                    "The headers must be comma-separated name=value pairs.");
        }

        return Task.CompletedTask;
    }

    // 不带查询串与片段：发送层在基地址后追加 /v1/{signal}
    private static bool IsHttpBaseAddress(string? value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment);

    private static bool IsHeaderList(string? value)
        => !string.IsNullOrWhiteSpace(value)
            && value.Split(',').All(pair =>
            {
                var separator = pair.IndexOf('=');
                return separator > 0 && IsToken(pair[..separator].Trim());
            });

    // HTTP 字段名（RFC 9110 token）
    private static bool IsToken(string name)
        => name.Length > 0 && name.All(c => char.IsAsciiLetterOrDigit(c) || "!#$%&'*+-.^_`|~".Contains(c));
}
