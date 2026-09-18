using Microsoft.Extensions.Localization;

namespace Leistd.ExceptionHandling.AspNetCore.Handlers;

// ProblemDetails 标题的取法：按 Title:{状态码} 查本地化词条，缺失时回退到英文。
// 业务异常与自动模型校验两条路径共用这一处，否则同一个状态码在两条路径上一条跟随语言、一条永远是英文。
// 本地化失败不得覆盖原本要返回的错误，因此查询全程吞异常、回退。
internal static class ProblemTitles
{
    // 未启用本地化、未命中或查询出错时返回 fallback（缺省为状态码的英文短语）。
    public static string Localize(IStringLocalizer? localizer, int statusCode, string? fallback = null)
    {
        fallback ??= Default(statusCode);
        if (localizer is null)
            return fallback;

        try
        {
            var localized = localizer["Title:" + statusCode];
            return localized.ResourceNotFound ? fallback : localized.Value;
        }
        catch
        {
            return fallback;
        }
    }

    public static string Default(int statusCode) => statusCode switch
    {
        400 => "Bad Request",
        401 => "Unauthorized",
        403 => "Forbidden",
        404 => "Not Found",
        409 => "Conflict",
        415 => "Unsupported Media Type",
        422 => "Unprocessable Entity",
        500 => "Internal Server Error",
        502 => "Bad Gateway",
        503 => "Service Unavailable",
        _ => "Error"
    };
}
