using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using Leistd.Localization.Core.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Leistd.Localization.Core.Json;

/// <summary>
/// 读取并缓存随程序集嵌入的 JSON 本地化资源。
/// </summary>
/// <remarks>
/// 资源文件结构（ABP 式）：<c>{ "culture": "en", "texts": { "Key": "Value" } }</c>；
/// 缺 <c>culture</c> 段的文件被忽略。同一 culture 的键在多个程序集出现时，
/// 按 <see cref="LeistdLocalizationOptions.ResourceAssemblies"/> 顺序后者覆盖前者。
/// 每个 culture 的合并结果按需加载一次并缓存。
/// </remarks>
public sealed class JsonLocalizationResourceReader(
    IOptions<LeistdLocalizationOptions> options,
    ILogger<JsonLocalizationResourceReader>? logger = null)
{
    private readonly LeistdLocalizationOptions _options = options.Value;
    private readonly ILogger _logger = logger ?? NullLogger<JsonLocalizationResourceReader>.Instance;
    private readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, string>> _cache = new();

    /// <summary>
    /// 取指定 culture 的全部键值（已合并各程序集、已缓存）。未找到资源时返回空字典。
    /// </summary>
    public IReadOnlyDictionary<string, string> GetTexts(string culture)
        => _cache.GetOrAdd(culture, LoadCulture);

    private IReadOnlyDictionary<string, string> LoadCulture(string culture)
    {
        var texts = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var assembly in _options.ResourceAssemblies)
        {
            var resourceName = ResolveResourceName(assembly, culture);
            if (resourceName is null)
                continue;

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream is null)
                continue;

            var parsed = Parse(stream, culture, resourceName);
            if (parsed is null)
                continue;

            // 后登记的程序集覆盖先前的同名键
            foreach (var pair in parsed)
                texts[pair.Key] = pair.Value;
        }

        return texts;
    }

    /// <summary>
    /// 在程序集嵌入清单中定位 <c>{ResourcesPath}.{culture}.json</c>（大小写不敏感，兼容目录分隔差异）。
    /// </summary>
    private string? ResolveResourceName(Assembly assembly, string culture)
    {
        var suffix = $".{culture}.json";
        var pathHint = _options.ResourcesPath.Replace('/', '.').Replace('\\', '.');

        foreach (var name in assembly.GetManifestResourceNames())
        {
            if (!name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                continue;
            if (string.IsNullOrEmpty(pathHint)
                || name.Contains($".{pathHint}.", StringComparison.OrdinalIgnoreCase))
                return name;
        }

        return null;
    }

    private IReadOnlyDictionary<string, string>? Parse(Stream stream, string expectedCulture, string resourceName)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(stream);
        }
        catch (JsonException ex)
        {
            // 坏文件不该拖垮整个本地化：跳过并告警，其余程序集/键正常加载
            _logger.LogWarning(ex, "本地化资源 {ResourceName} 不是合法 JSON，已跳过。", resourceName);
            return null;
        }

        using (document)
        {
            var root = document.RootElement;

            // 缺 culture 段的文件忽略（与 ABP 行为一致）
            if (!root.TryGetProperty("culture", out var cultureElement)
                || cultureElement.ValueKind != JsonValueKind.String)
                return null;

            // 声明的 culture 应与文件名解析出的 culture 一致，否则很可能是复制粘贴漏改 → 告警但仍加载
            var declaredCulture = cultureElement.GetString();
            if (!string.Equals(declaredCulture, expectedCulture, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning(
                    "本地化资源 {ResourceName} 声明的 culture '{Declared}' 与文件名 culture '{Expected}' 不一致，请核对。",
                    resourceName,
                    declaredCulture,
                    expectedCulture);
            }

            if (!root.TryGetProperty("texts", out var textsElement)
                || textsElement.ValueKind != JsonValueKind.Object)
                return null;

            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var property in textsElement.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.String)
                    result[property.Name] = property.Value.GetString()!;
            }

            return result;
        }
    }
}
