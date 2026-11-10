using System.Collections;
using Leistd.Tracing.Constants;

namespace Leistd.Tracing.Services;

// 关联标识日志作用域的状态，HTTP 中间件与环境上下文贡献者共用：提供程序按键值枚举采集结构化作用域
// （JSON、OpenTelemetry），Simple 控制台格式只调用 ToString()，普通字典在那里只打印类型名，因此输出 键:值。
internal sealed class CorrelationIdLogScope(string correlationId) : IReadOnlyList<KeyValuePair<string, object?>>
{
    public int Count => 1;

    public KeyValuePair<string, object?> this[int index] => index == 0
        ? new KeyValuePair<string, object?>(CorrelationIdConstants.LogKey, correlationId)
        : throw new ArgumentOutOfRangeException(nameof(index));

    public IEnumerator<KeyValuePair<string, object?>> GetEnumerator()
    {
        yield return this[0];
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public override string ToString() => $"{CorrelationIdConstants.LogKey}:{correlationId}";
}
