using System.Security.Cryptography;
using Leistd.MultiTenancy.EntityFrameworkCore.ConnectionStrings;
using Leistd.MultiTenancy.Exceptions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Xunit;

namespace Leistd.MultiTenancy.Tests.EntityFrameworkCore;

/// <summary>
/// 租户连接串解不开时的诊断：专用异常带租户、连接名、用途与密钥标识，唯一解密点记一条固定事件号的 Error。
/// 运维靠密钥标识区分“缺某把密钥”（有标识）与“数据本身坏了”（标识为空）。
/// </summary>
public sealed class TenantConnectionStringDecryptionTests : IDisposable
{
    private const string Secret = "Pa55word-do-not-leak";
    private const string ConnectionString = $"Host=tenant-db;Database=acme;Password={Secret}";
    private const string Name = "billing";

    private readonly DirectoryInfo _keys = Directory.CreateTempSubdirectory("leistd-tenant-keys-");
    private readonly FakeLoggerProvider _logs = new();
    private readonly ILoggerFactory _loggerFactory;

    public TenantConnectionStringDecryptionTests() => _loggerFactory = new LoggerFactory([_logs]);

    public void Dispose()
    {
        _loggerFactory.Dispose();
        _keys.Delete(recursive: true);
    }

    // 持久化密钥环写一把密钥文件 key-{id}.xml；从文件名取到的标识与密文头无关，用来对照读取结果
    private (string Ciphertext, Guid KeyId) ProtectWithPersistedKeyRing()
    {
        var ciphertext = new TenantConnectionStringProtector(DataProtectionProvider.Create(_keys)).Protect(ConnectionString);
        var keyFile = Assert.Single(_keys.GetFiles("key-*.xml"));
        return (ciphertext, Guid.Parse(Path.GetFileNameWithoutExtension(keyFile.Name)["key-".Length..]));
    }

    private TenantConnectionStringProtector ForeignKeyRing() =>
        new(new EphemeralDataProtectionProvider(), _loggerFactory);

    [Fact]
    public void The_key_id_is_read_from_the_ciphertext_header()
    {
        var (ciphertext, keyId) = ProtectWithPersistedKeyRing();

        Assert.Equal(keyId, ProtectedPayload.TryReadKeyId(ciphertext));
    }

    // 不是 Data Protection 密文（不是 base64url、魔数不对）或被截断到头部不完整：读不出就为空，不抛
    [Theory]
    [InlineData("not-a-valid-payload")]
    [InlineData("")]
    [InlineData("%%%%")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public void A_value_that_is_not_a_data_protection_payload_has_no_key_id(string value)
    {
        Assert.Null(ProtectedPayload.TryReadKeyId(value));
    }

    [Fact]
    public void A_truncated_ciphertext_has_no_key_id()
    {
        var (ciphertext, _) = ProtectWithPersistedKeyRing();

        // 魔数 4 字节 + 密钥标识 16 字节 = 20 字节，base64url 至少 27 个字符；截到 20 个字符只剩约 15 字节
        Assert.Null(ProtectedPayload.TryReadKeyId(ciphertext[..20]));
    }

    [Fact]
    public void A_ciphertext_from_another_key_ring_throws_the_dedicated_exception_with_its_key_id()
    {
        var (ciphertext, keyId) = ProtectWithPersistedKeyRing();
        var tenantId = Guid.NewGuid();

        var error = Assert.Throws<TenantConnectionStringDecryptionException>(
            () => ForeignKeyRing().Unprotect(tenantId, Name, ciphertext));

        Assert.Equal(tenantId, error.TenantId);
        Assert.Equal(Name, error.ConnectionName);
        Assert.Equal(TenantConnectionStringProtector.Purpose, error.Purpose);
        Assert.Equal(keyId, error.KeyId);
        Assert.Contains(keyId.ToString("D"), error.Message);
        // 现有的 catch (InvalidOperationException) 照样接得住，原始失败保留在内层
        Assert.IsAssignableFrom<InvalidOperationException>(error);
        Assert.IsAssignableFrom<CryptographicException>(error.InnerException);
        Assert.DoesNotContain(Secret, error.ToString());
        Assert.DoesNotContain(ciphertext, error.ToString());
    }

    [Fact]
    public void A_ciphertext_from_another_key_ring_logs_the_fixed_event_with_structured_fields()
    {
        var (ciphertext, keyId) = ProtectWithPersistedKeyRing();
        var tenantId = Guid.NewGuid();

        Assert.Throws<TenantConnectionStringDecryptionException>(() => ForeignKeyRing().Unprotect(tenantId, Name, ciphertext));

        var record = Assert.Single(_logs.Collector.GetSnapshot());
        Assert.Equal(4301, record.Id.Id);
        Assert.Equal(MultiTenancyLogEvents.TenantConnectionStringUnprotectFailed, record.Id.Id);
        Assert.Equal("TenantConnectionStringUnprotectFailed", record.Id.Name);
        Assert.Equal(LogLevel.Error, record.Level);
        Assert.Equal(typeof(TenantConnectionStringProtector).FullName, record.Category);
        Assert.Equal(TenantConnectionStringProtector.Purpose, record.GetStructuredStateValue("Purpose"));
        Assert.Equal(tenantId.ToString(), record.GetStructuredStateValue("TenantId"));
        Assert.Equal(Name, record.GetStructuredStateValue("ConnectionName"));
        Assert.Equal(keyId.ToString(), record.GetStructuredStateValue("KeyId"));
        Assert.IsAssignableFrom<CryptographicException>(record.Exception);
        // 不记密文、明文与连接串
        Assert.DoesNotContain(record.StructuredState!, pair =>
            pair.Value is { } value && (value.Contains(Secret, StringComparison.Ordinal) || value == ciphertext));
        Assert.DoesNotContain(Secret, record.Message);
        Assert.DoesNotContain(ciphertext, record.Message);
    }

    // 被改动的数据读不出头部：异常与事件的密钥标识都为空，与“缺密钥”区分开
    [Fact]
    public void A_tampered_value_has_no_key_id_in_the_exception_or_the_event()
    {
        var tenantId = Guid.NewGuid();

        var error = Assert.Throws<TenantConnectionStringDecryptionException>(
            () => ForeignKeyRing().Unprotect(tenantId, Name, "not-a-valid-payload"));

        Assert.Null(error.KeyId);
        var record = Assert.Single(_logs.Collector.GetSnapshot());
        Assert.Equal(MultiTenancyLogEvents.TenantConnectionStringUnprotectFailed, record.Id.Id);
        Assert.Null(record.GetStructuredStateValue("KeyId"));
        Assert.Equal(tenantId.ToString(), record.GetStructuredStateValue("TenantId"));
    }
}
