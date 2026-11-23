namespace Leistd.Settings.Exceptions;

/// <summary>落过库的机密设置用当前 Data Protection 密钥环解不开。</summary>
/// <remarks>
/// <para>原因多为写入它的密钥已不在密钥环里、本进程没有共享写入方的密钥环或应用名，或密文被改动；内层异常是
/// Data Protection 的 <see cref="System.Security.Cryptography.CryptographicException"/>。读取不回退到默认值。</para>
/// <para>继承 <see cref="InvalidOperationException"/>，原有的捕获照常生效。异常与日志都不带密文和明文。</para>
/// </remarks>
public sealed class SettingDecryptionException : InvalidOperationException
{
    /// <summary>构造解密失败异常。</summary>
    /// <param name="settingName">设置名。</param>
    /// <param name="purposes">保护器的用途链，依次是组件用途与设置名。</param>
    /// <param name="keyId">密文头里记录的密钥标识；读不出时为 <see langword="null"/>。</param>
    /// <param name="innerException">Data Protection 抛出的原始异常。</param>
    public SettingDecryptionException(
        string settingName,
        IReadOnlyList<string> purposes,
        Guid? keyId,
        Exception innerException)
        : base(
            $"Setting '{settingName}' could not be decrypted with the current Data Protection key ring" +
            (keyId is { } id ? $" (key {id:D})" : string.Empty) + ". " +
            "Check that this process uses the key ring and application name of the process that wrote it " +
            "(the host's Data Protection, or SettingEncryptionOptions.DataProtectionProvider when set).",
            innerException)
    {
        SettingName = settingName;
        Purposes = purposes;
        KeyId = keyId;
    }

    /// <summary>解不开的设置名。</summary>
    public string SettingName { get; }

    /// <summary>保护器的用途链：组件用途 <c>Leistd.Settings.EncryptedValue.v1</c> 与设置名。改名会让旧密文解不开。</summary>
    public IReadOnlyList<string> Purposes { get; }

    /// <summary>密文头记录的密钥标识，用来查是哪一把密钥缺失；密文不是标准 Data Protection 格式时为 <see langword="null"/>。</summary>
    public Guid? KeyId { get; }
}
