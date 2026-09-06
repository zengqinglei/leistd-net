namespace Leistd.Tracing.Attributes;

/// <summary>
/// 为方法或类型启用自动 TraceId 作用域。
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, Inherited = true)]
public class CorrelationIdAttribute : Attribute
{
}
