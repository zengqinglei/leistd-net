using Leistd.Authorization.Constants;
using Leistd.Authorization.Resource.Grants;
using Leistd.ExceptionHandling;

namespace Leistd.Authorization.Resource.Exceptions;

/// <summary>
/// 表示资源授权主体或效果无效。
/// </summary>
/// <remarks>
/// 两类非法写入合并为一个类型，差别由 <see cref="Reason"/> 表达（处置方式相同：修调用方传进来的数据）。
/// 必须在写入口拒掉：未知 ProviderName 会成为永远匹配不上的脏数据，
/// 而非法枚举效果一旦落库就把该资源静默变成"任何人都不许访问"。
/// </remarks>
/// <param name="resourceName">资源类型名。</param>
/// <param name="resourceKey">资源实例 Key。</param>
/// <param name="reason">具体原因，进入异常消息。</param>
public sealed class InvalidResourceGrantException(string resourceName, string resourceKey, string reason)
    : BadRequestException($"Resource grant on '{resourceName}/{resourceKey}' is invalid: {reason}")
{
    /// <summary>资源类型名。</summary>
    public string ResourceName { get; } = resourceName;

    /// <summary>资源实例 Key。</summary>
    public string ResourceKey { get; } = resourceKey;

    /// <summary>具体原因。</summary>
    public string Reason { get; } = reason;
}
