using Leistd.Authorization.Constants;
using Leistd.Authorization.Resource.Grants;
using Leistd.ExceptionHandling;

namespace Leistd.Authorization.Resource.Exceptions;

/// <summary>
/// 表示资源授权主体或效果无效。
/// </summary>
/// <remarks>
/// <see cref="Reason"/> 区分主体与效果校验失败，由调用方修正输入。
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
