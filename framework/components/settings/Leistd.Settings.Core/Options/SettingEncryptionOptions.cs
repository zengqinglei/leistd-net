using Microsoft.AspNetCore.DataProtection;

namespace Leistd.Settings.Options;

/// <summary>机密设置（<see cref="Definitions.ISettingDefinition.IsEncrypted"/>）的加解密约定。</summary>
public sealed class SettingEncryptionOptions
{
    /// <summary>加解密机密设置用的 Data Protection；<see langword="null"/> 时用容器里宿主的 <see cref="IDataProtectionProvider"/>。</summary>
    /// <remarks>
    /// <para>多个应用名不同的宿主要读写同一批机密设置时，给每个宿主设同一个专用实例：
    /// 应用名与密钥存储相同，宿主自己的会话类密钥环（Cookie、防伪令牌）保持隔离。
    /// 写法同 <c>CookieAuthenticationOptions.DataProtectionProvider</c>，实例用 <c>DataProtectionProvider.Create</c> 创建。</para>
    /// <para>设了之后宿主密钥环里的旧密文不再能解；已有机密设置要在切换后重新写入。</para>
    /// <para>不绑定配置节：值是服务实例，经 <c>services.Configure&lt;SettingEncryptionOptions&gt;</c> 设置。</para>
    /// </remarks>
    public IDataProtectionProvider? DataProtectionProvider { get; set; }
}
