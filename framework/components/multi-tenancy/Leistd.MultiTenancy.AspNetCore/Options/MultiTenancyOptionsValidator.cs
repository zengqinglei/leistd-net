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

        // 把占位符换成一个合法 label 后做结构化主机名校验。
        // 逐个排查非法字符是补不完的黑名单（?、#、@、反斜杠、空 label、其它占位符……），
        // 而这里要判定的本来就是"替换后是不是一个合法主机名"——交给 BCL 一次判完。
        var probe = format.Replace(TenantPlaceholder, SampleLabel, StringComparison.Ordinal);
        if (Uri.CheckHostName(probe) != UriHostNameType.Dns)
        {
            failures.Add("不是合法的主机名形态：格式只匹配主机名，不能含协议、端口、路径、查询串、片段或空 label。");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                $"MultiTenancyOptions.DomainFormat 配置非法（'{format}'）：{string.Join(" ", failures)}");
    }
}
