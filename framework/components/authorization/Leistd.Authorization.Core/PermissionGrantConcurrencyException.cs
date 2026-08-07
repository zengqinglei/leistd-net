namespace Leistd.Authorization;

/// <summary>
/// 权限授予乐观并发冲突：调用方持有的版本已过期，说明另一次写入抢先完成。
/// </summary>
/// <remarks>
/// 宿主应把它映射为 HTTP 409，并提示调用方重新加载后再提交，而不是静默覆盖对方的修改。
/// 本组件不依赖任何 HTTP 或异常映射组件，因此映射由宿主完成。
/// </remarks>
public class PermissionGrantConcurrencyException : System.Exception
{
    /// <summary>
    /// 初始化异常。
    /// </summary>
    /// <param name="providerName">授予对象类型。</param>
    /// <param name="providerKey">授予对象 Key。</param>
    /// <param name="expectedRevision">调用方期望的版本。</param>
    /// <param name="actualRevision">存储中的实际版本。</param>
    public PermissionGrantConcurrencyException(
        string providerName,
        string providerKey,
        long expectedRevision,
        long actualRevision)
        : base($"权限授予版本冲突：{providerName}/{providerKey} 期望版本 {expectedRevision}，实际版本 {actualRevision}。")
    {
        ProviderName = providerName;
        ProviderKey = providerKey;
        ExpectedRevision = expectedRevision;
        ActualRevision = actualRevision;
    }

    /// <summary>授予对象类型。</summary>
    public string ProviderName { get; }

    /// <summary>授予对象 Key。</summary>
    public string ProviderKey { get; }

    /// <summary>调用方期望的版本。</summary>
    public long ExpectedRevision { get; }

    /// <summary>存储中的实际版本。</summary>
    public long ActualRevision { get; }
}
