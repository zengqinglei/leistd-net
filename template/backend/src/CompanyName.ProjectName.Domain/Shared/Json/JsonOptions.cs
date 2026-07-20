using System.Text.Json;
using System.Text.Json.Serialization;

namespace CompanyName.ProjectName.Domain.Shared.Json;

/// <summary>
/// 统一的 JSON 序列化选项配置
/// </summary>
public static class JsonOptions
{
    /// <summary>
    /// 默认选项：不区分大小写
    /// </summary>
    public static readonly JsonSerializerOptions Default = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// 配置文件选项：不区分大小写 + 允许尾随逗号
    /// </summary>
    public static readonly JsonSerializerOptions Configuration = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true
    };

    /// <summary>
    /// Snake Case 选项（用于 Google/Claude 等 API）
    /// </summary>
    public static readonly JsonSerializerOptions SnakeCase = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// 紧凑格式选项（不缩进）
    /// </summary>
    public static readonly JsonSerializerOptions Compact = new()
    {
        WriteIndented = false
    };

    /// <summary>
    /// Web API 选项：枚举字符串化 + 驼峰命名 + 忽略空值
    /// 用于 ASP.NET Core Controllers 的 JSON 序列化配置
    /// </summary>
    public static readonly JsonSerializerOptions WebApi = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters =
        {
            JsonConverters.StringEnumConverter  // 使用字符串枚举转换器
        }
    };

    /// <summary>
    /// 把 Web API 序列化策略应用到给定的 options 实例——单一数据源，供 MVC（AddJsonOptions）与
    /// HTTP 管道（ConfigureHttpJsonOptions，ProblemDetails/IProblemDetailsService 走此配置）共用，
    /// 确保业务响应、400、422 的命名策略一致；宿主改此处即全部跟随。
    /// </summary>
    public static void ConfigureWebApi(JsonSerializerOptions options)
    {
        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        options.Converters.Add(JsonConverters.StringEnumConverter);
    }
}
