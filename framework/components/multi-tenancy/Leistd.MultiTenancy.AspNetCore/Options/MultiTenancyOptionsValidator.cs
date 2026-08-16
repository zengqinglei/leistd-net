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

    /// <summary>校验时替换占位符用的示例 label：只要它合法，租户名的合法性由运行期解析保证</summary>
    private const string SampleLabel = "t";

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

        // 把占位符换成一个合法 label 后校验主机名形态。
        //
        // 这里刻意不用 Uri.CheckHostName：它对 DNS 名的判定比浏览器实际发送的 Host 宽松，
        // 会放行 t_.example.com、t-.example.com、t.example-.com 这类**永远匹配不上**的格式，
        // 于是解析静默退回请求头——正是本校验要关掉的那条路。逐 label 明确判定反而更短也更准。
        var probe = format.Replace(TenantPlaceholder, SampleLabel, StringComparison.Ordinal);
        failures.AddRange(HostNameFailures(probe));

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                $"MultiTenancyOptions.DomainFormat 配置非法（'{format}'）：{string.Join(" ", failures)}");
    }

    /// <summary>
    /// 按浏览器实际会发送的 Host 形态校验：ASCII、逐 label 长度与首尾字符约束
    /// </summary>
    /// <remarks>
    /// **只接受 ASCII/punycode**。国际化域名不做启动期规范化，而是要求配置方直接写 punycode 形态
    /// （<c>xn--</c> 前缀）：浏览器发来的 Host 本就是 punycode，两边写成同一种形态才能匹配；
    /// 若这里接受 Unicode 而运行期做字面比较，配置看着对、请求永远落不进来。
    /// </remarks>
    private static IEnumerable<string> HostNameFailures(string host)
    {
        if (host.Any(c => c > 127))
        {
            yield return "含非 ASCII 字符：国际化域名请填 punycode 形态（xn-- 前缀），与浏览器发送的 Host 一致。";
            yield break;
        }

        // 主机名总长上限（RFC 1035）
        if (host.Length > 253)
        {
            yield return "主机名超过 253 个字符。";
        }

        var labels = host.Split('.');
        if (labels.Length < 2)
        {
            yield return "至少需要两段（例如 '{0}.example.com'）。";
        }

        foreach (var label in labels)
        {
            if (label.Length is 0 or > 63)
            {
                yield return $"存在空或超过 63 字符的段：'{label}'。";
                continue;
            }

            if (!char.IsAsciiLetterOrDigit(label[0]) || !char.IsAsciiLetterOrDigit(label[^1]))
            {
                yield return $"段 '{label}' 必须以字母或数字开头和结尾。";
                continue;
            }

            if (label.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-'))
            {
                yield return $"段 '{label}' 只能包含字母、数字与连字符。";
            }
        }
    }
}
