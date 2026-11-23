namespace Leistd.MultiTenancy.Exceptions;

/// <summary>落过库的租户连接串用当前 Data Protection 密钥环解不开。</summary>
/// <remarks>
/// <para>原因多为写入它的密钥已不在密钥环里或已被吊销、本进程没有共享写入方的密钥环或应用名，或密文被改动；内层异常是
/// Data Protection 的 <see cref="System.Security.Cryptography.CryptographicException"/>。解析不回退到宿主自己的库。</para>
/// <para>继承 <see cref="InvalidOperationException"/>，原有的捕获照常生效。框架生成的异常消息与日志事件字段不带密文、明文和连接串；内层异常照原样保留。</para>
/// </remarks>
public sealed class TenantConnectionStringDecryptionException : InvalidOperationException
{
    /// <summary>构造解密失败异常。</summary>
    /// <param name="tenantId">连接所属的租户。</param>
    /// <param name="connectionName">登记的连接名。</param>
    /// <param name="purpose">解密所用保护器的用途。</param>
    /// <param name="keyId">密文头里记录的密钥标识；读不出时为 <see langword="null"/>。</param>
    /// <param name="innerException">Data Protection 抛出的原始异常。</param>
    public TenantConnectionStringDecryptionException(
        Guid tenantId,
        string connectionName,
        string purpose,
        Guid? keyId,
        Exception innerException)
        : base(
            $"The '{connectionName}' connection string of tenant '{tenantId}' could not be decrypted with the current " +
            "Data Protection key ring" + (keyId is { } id ? $" (key {id:D})" : string.Empty) + ". " +
            "Check that this process shares the key ring and application name of the process that wrote it.",
            innerException)
    {
        TenantId = tenantId;
        ConnectionName = connectionName;
        Purpose = purpose;
        KeyId = keyId;
    }

    /// <summary>连接所属的租户标识。</summary>
    public Guid TenantId { get; }

    /// <summary>解不开的连接名，即登记行上的名字。</summary>
    public string ConnectionName { get; }

    /// <summary>保护器用途 <c>Leistd.MultiTenancy.TenantConnectionString.v1</c>；用途或应用名不同的密钥环解不开旧密文。</summary>
    public string Purpose { get; }

    /// <summary>从密文头读出、未经验证的密钥标识，用来对照本进程的密钥环；头部读不出（不是标准 Data Protection 格式或头部被截断）时为 <see langword="null"/>，头部完整而之后的内容损坏时仍有值。</summary>
    public Guid? KeyId { get; }
}
