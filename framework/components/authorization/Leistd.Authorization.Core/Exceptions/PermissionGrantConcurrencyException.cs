using Leistd.ExceptionHandling;

namespace Leistd.Authorization.Exceptions;

/// <summary>
/// 表示权限授予发生乐观并发冲突。
/// </summary>
/// <remarks>
/// 映射为 409；宿主可捕获后用 <c>WithCode</c> 指定错误码与展示文案。
/// </remarks>
public class PermissionGrantConcurrencyException : ConflictException
{
    /// <summary>
    /// 初始化异常。
    /// </summary>
    /// <param name="providerName">授予对象类型。</param>
    /// <param name="providerKey">授予对象 Key。</param>
    /// <param name="expectedVersion">调用方期望的版本。</param>
    /// <param name="actualVersion">存储中的实际版本。</param>
    public PermissionGrantConcurrencyException(
        string providerName,
        string providerKey,
        long expectedVersion,
        long actualVersion)
        : base($"Permission grant version conflict for {providerName}/{providerKey}: " +
               $"expected {expectedVersion} but found {actualVersion}. Reload and try again.")
    {
        ProviderName = providerName;
        ProviderKey = providerKey;
        ExpectedVersion = expectedVersion;
        ActualVersion = actualVersion;
    }

    /// <summary>授予对象类型。</summary>
    public string ProviderName { get; }

    /// <summary>授予对象 Key。</summary>
    public string ProviderKey { get; }

    /// <summary>调用方期望的版本。</summary>
    public long ExpectedVersion { get; }

    /// <summary>存储中的实际版本。</summary>
    public long ActualVersion { get; }
}
