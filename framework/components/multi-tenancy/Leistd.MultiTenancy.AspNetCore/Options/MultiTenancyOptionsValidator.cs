using Microsoft.Extensions.Options;

namespace Leistd.MultiTenancy;

/// <summary>
/// 启动期校验 <see cref="MultiTenancyOptions"/>，非法配置直接阻止宿主启动
/// </summary>
/// <remarks>
/// <para><b>为什么必须在启动期失败。</b><see cref="MultiTenancyOptions.DomainFormat"/> 一旦配置，
/// 子域名就是匿名请求的**权威来源**，排在请求头之前。而写错的格式（漏了占位符、
/// 带上 scheme 或端口、多写一个 <c>{0}</c>）在运行期只表现为"永不匹配"，
/// 于是解析静默退回到请求头与查询串——恰恰是这个配置想要收紧的那两条路径。</para>
/// <para>这是 fail-open：安全配置写错了却什么都不报，系统看起来在跑、边界已经没了。
/// 与权限定义重名在启动期抛异常是同一条约定：配置错误要大声失败，不要静默降级。</para>
/// </remarks>
public class MultiTenancyOptionsValidator : IValidateOptions<MultiTenancyOptions>
{
    private const string TenantPlaceholder = "{0}";

    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, MultiTenancyOptions options)
    {
        var format = options.DomainFormat;
        if (string.IsNullOrWhiteSpace(format))
        {
            // 不配置即不启用子域名解析，是合法状态
            return ValidateOptionsResult.Success;
        }

        var failures = new List<string>();

        var first = format.IndexOf(TenantPlaceholder, StringComparison.Ordinal);
        if (first < 0)
        {
            failures.Add($"缺少租户占位符 '{TenantPlaceholder}'，例如 '{{0}}.example.com'。");
        }
        else if (format.IndexOf(TenantPlaceholder, first + TenantPlaceholder.Length, StringComparison.Ordinal) >= 0)
        {
            failures.Add($"包含多个 '{TenantPlaceholder}'，无法确定哪一段是租户名。");
        }

        if (format.Contains("://", StringComparison.Ordinal))
        {
            failures.Add("不应包含协议（scheme）：格式只匹配主机名。");
        }

        if (format.Contains('/'))
        {
            failures.Add("不应包含路径：格式只匹配主机名。");
        }

        // 端口写进格式会让开发（:5240）与生产各配一份，而解析本身不看端口
        if (format.Contains(':'))
        {
            failures.Add("不应包含端口：解析只取主机名，端口不参与匹配。");
        }

        if (format.Any(char.IsWhiteSpace))
        {
            failures.Add("不应包含空白字符。");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                $"MultiTenancyOptions.DomainFormat 配置非法（'{format}'）：{string.Join(" ", failures)}");
    }
}
