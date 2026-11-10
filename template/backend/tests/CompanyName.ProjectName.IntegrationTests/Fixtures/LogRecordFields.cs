using Microsoft.Extensions.Logging.Testing;

namespace CompanyName.ProjectName.IntegrationTests.Fixtures;

/// <summary>按 <see cref="FakeLogRecord"/> 的公开契约读取字段：消息状态与作用域里的键值，结构化值为字符串。</summary>
internal static class LogRecordFields
{
    /// <summary>消息状态里的字段；没有该键时为 <see langword="null"/>。</summary>
    public static string? State(FakeLogRecord record, string key) => record.GetStructuredStateValue(key);

    /// <summary>消息状态里是否有该键（值可以为 <see langword="null"/>）。</summary>
    public static bool HasState(FakeLogRecord record, string key)
        => record.StructuredState?.Any(pair => pair.Key == key) == true;

    /// <summary>作用域里的字段：取最内层带该键的作用域；没有时为 <see langword="null"/>。</summary>
    /// <remarks>作用域状态只按键值枚举读取，不依赖它的具体类型与文本形态。</remarks>
    public static string? Scope(FakeLogRecord record, string key)
    {
        for (var index = record.Scopes.Count - 1; index >= 0; index--)
        {
            if (record.Scopes[index] is IEnumerable<KeyValuePair<string, object?>> pairs)
            {
                foreach (var (name, value) in pairs)
                {
                    if (name == key)
                        return value?.ToString();
                }
            }
        }

        return null;
    }
}
