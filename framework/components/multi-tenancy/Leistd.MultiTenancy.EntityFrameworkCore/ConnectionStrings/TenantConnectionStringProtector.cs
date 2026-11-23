using System.Security.Cryptography;
using Leistd.MultiTenancy.Exceptions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;

namespace Leistd.MultiTenancy.EntityFrameworkCore.ConnectionStrings;

// 连接串写入时加密、读取时解密；密钥环沿用宿主的 Data Protection 配置，本组件不管理密钥。
// purpose 固定：改动会让所有已存密文不可解密。
// 这里是唯一的解密点（配置存储、迁移清单、库目录、本地解析都经过它），解密失败的事件只在这里记一次
internal sealed class TenantConnectionStringProtector(IDataProtectionProvider provider, ILoggerFactory? loggerFactory = null)
{
    public const string Purpose = "Leistd.MultiTenancy.TenantConnectionString.v1";

    private readonly IDataProtector _protector = provider.CreateProtector(Purpose);

    // 各调用点共用一个日志类别，告警按类别加事件号匹配时不随调用点变化
    private readonly ILogger? _logger = loggerFactory?.CreateLogger<TenantConnectionStringProtector>();

    public string Protect(string connectionString) => _protector.Protect(connectionString);

    public string Unprotect(Guid tenantId, string name, string protectedConnectionString)
    {
        try
        {
            return _protector.Unprotect(protectedConnectionString);
        }
        catch (CryptographicException exception)
        {
            // 密钥丢失、密钥环未共享或密文被篡改；拒绝而不是回退到宿主自己的库。只记租户、连接名与密钥标识，不记密文与明文
            var keyId = ProtectedPayload.TryReadKeyId(protectedConnectionString);
            _logger?.LogError(
                new EventId(
                    MultiTenancyLogEvents.TenantConnectionStringUnprotectFailed,
                    nameof(MultiTenancyLogEvents.TenantConnectionStringUnprotectFailed)),
                exception,
                "The {ConnectionName} connection string of tenant {TenantId} could not be decrypted with the current " +
                "Data Protection key ring (purpose {Purpose}, key {KeyId}).",
                name,
                tenantId,
                Purpose,
                keyId);
            throw new TenantConnectionStringDecryptionException(tenantId, name, Purpose, keyId, exception);
        }
    }
}
