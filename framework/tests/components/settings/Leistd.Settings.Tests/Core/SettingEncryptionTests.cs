using Leistd.Settings.Definitions;
using Leistd.Settings.Management;
using Leistd.Security.Users;
using Leistd.Settings.Options;
using Leistd.Settings.Resolution;
using Leistd.Settings.Stores;
using Leistd.TestBase.Doubles;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;
using Leistd.Settings.Tests.TestDoubles;

namespace Leistd.Settings.Tests.Core;

// 机密设置：经宿主（或选项指定的专用）Data Protection 加密落库、读出解密，永不经"客户端可见"下发。
public class SettingEncryptionTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private readonly IDataProtectionProvider _dataProtection = new EphemeralDataProtectionProvider();

    [Fact]
    public async Task An_encrypted_setting_is_stored_as_cipher_text_and_read_back_as_plain_text()
    {
        var store = new FakeSettingStore();

        await Manager(store, _dataProtection).SetAsync("Email.Password", "s3cret", SettingScopes.Host);

        var stored = Assert.Single(store.Writes).Value!;
        Assert.NotEqual("s3cret", stored);
        store.Host["Email.Password"] = stored;
        Assert.Equal("s3cret", await Provider(store, _dataProtection).GetOrNullAsync("Email.Password"));
    }

    // 清除不是写入一个值，没有东西可加密
    [Fact]
    public async Task Clearing_an_encrypted_setting_writes_null()
    {
        var store = new FakeSettingStore();

        await Manager(store, _dataProtection).SetAsync("Email.Password", null, SettingScopes.Host);

        Assert.Null(Assert.Single(store.Writes).Value);
    }

    // 默认值按约定不加密，原样返回；拿去解密会因为"不是密文"而失败
    [Fact]
    public async Task The_default_value_of_an_encrypted_setting_is_not_decrypted()
    {
        Assert.Equal("fallback", await Provider(new FakeSettingStore(), _dataProtection).GetOrNullAsync("Api.Token"));
    }

    // 用途带设置名：一个设置的密文挪到别的设置名下解不开，不能靠挪行换值
    [Fact]
    public async Task Cipher_text_is_bound_to_the_setting_name()
    {
        var store = new FakeSettingStore();
        await Manager(store, _dataProtection).SetAsync("Email.Password", "s3cret", SettingScopes.Host);
        store.Tenant["Api.Token"] = Assert.Single(store.Writes).Value!;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Provider(store, _dataProtection).GetOrNullAsync("Api.Token"));
    }

    // 没配 Data Protection：机密设置宁可失败也不存明文；普通设置不受影响
    [Fact]
    public async Task Without_data_protection_only_encrypted_settings_fail()
    {
        var store = new FakeSettingStore();
        var manager = Manager(store, dataProtection: null);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => manager.SetAsync("Email.Password", "s3cret", SettingScopes.Host));
        Assert.Empty(store.Writes);

        await manager.SetAsync("Email.Host", "smtp.example.com", SettingScopes.Host);
        Assert.Equal("smtp.example.com", Assert.Single(store.Writes).Value);
    }

    // 可见标记只表示界面上有这一项可写；按"客户端可见"批量读取时它不能出现，也不去解密
    [Fact]
    public async Task Encrypted_settings_are_never_returned_as_visible_to_clients()
    {
        var store = new FakeSettingStore();
        await Manager(store, _dataProtection).SetAsync("Email.Password", "s3cret", SettingScopes.Host);
        store.Host["Email.Password"] = Assert.Single(store.Writes).Value!;

        // 没有 Data Protection 也能批量读：机密设置不在其中，就不会去解密
        var visible = await Provider(store, dataProtection: null).GetAllAsync(visibleToClientsOnly: true);
        var all = await Provider(store, _dataProtection).GetAllAsync();

        Assert.False(visible.ContainsKey("Email.Password"));
        Assert.Equal("s3cret", all["Email.Password"]);
    }

    // 两个宿主应用名不同、各自的密钥环互不相通：都设同一个专用实例后，一方写入的机密另一方读得出；
    // 没设专用实例的宿主仍用自己的密钥环，解不开
    [Fact]
    public async Task Hosts_with_separate_key_rings_share_secrets_through_a_dedicated_data_protection()
    {
        var keys = Directory.CreateTempSubdirectory("leistd-settings-keys-");
        try
        {
            var store = new FakeSettingStore();
            await Manager(store, new EphemeralDataProtectionProvider(), Encryption(Dedicated(keys)))
                .SetAsync("Api.Token", "s3cret", SettingScopes.Tenant);
            store.Tenant["Api.Token"] = Assert.Single(store.Writes).Value!;

            var readerHostRing = new EphemeralDataProtectionProvider();
            Assert.Equal("s3cret", await Provider(store, readerHostRing, Encryption(Dedicated(keys))).GetOrNullAsync("Api.Token"));
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => Provider(store, readerHostRing).GetOrNullAsync("Api.Token"));
        }
        finally
        {
            keys.Delete(recursive: true);
        }
    }

    // 专用实例经 AddSettingsCore 登记的选项生效，不需要宿主注册别的服务
    [Fact]
    public async Task The_dedicated_data_protection_is_picked_up_from_the_container()
    {
        var store = new FakeSettingStore();
        var dedicated = new EphemeralDataProtectionProvider();
        await Manager(store, new EphemeralDataProtectionProvider(), Encryption(dedicated))
            .SetAsync("Api.Token", "s3cret", SettingScopes.Tenant);
        store.Tenant["Api.Token"] = Assert.Single(store.Writes).Value!;

        var services = new ServiceCollection().AddSettingsCore();
        services.AddSingleton<ISettingStore>(store);
        services.AddSingleton<ICurrentUser>(new FakeCurrentUser(UserId));
        services.AddSingleton<ISettingDefinitionProvider, EncryptionTestDefinitionProvider>();
        services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        services.Configure<SettingEncryptionOptions>(options => options.DataProtectionProvider = dedicated);
        await using var container = services.BuildServiceProvider();
        await using var scope = container.CreateAsyncScope();

        Assert.Equal("s3cret", await scope.ServiceProvider.GetRequiredService<ISettingProvider>().GetOrNullAsync("Api.Token"));
    }

    // 同一作用域先写后读：写入入口作废读取器的记忆化结果，读到的不是写入前的快照
    [Fact]
    public async Task A_write_invalidates_the_same_scope_reader()
    {
        var store = new FakeSettingStore();
        var provider = Provider(store, _dataProtection);
        var manager = new DefaultSettingManager(Definitions(), store, provider, [], WriteDependencies.Lock(), new RecordingUnitOfWorkManager(), dataProtectionProvider: _dataProtection);

        Assert.Null(await provider.GetOrNullAsync("Email.Host"));
        Assert.Equal(1, store.HostReads);

        await manager.SetAsync("Email.Host", "smtp.example.com", SettingScopes.Host);
        store.Host["Email.Host"] = "smtp.example.com";

        Assert.Equal("smtp.example.com", await provider.GetOrNullAsync("Email.Host"));
        Assert.Equal(2, store.HostReads);
    }

    private static ISettingManager Manager(
        FakeSettingStore store,
        IDataProtectionProvider? dataProtection,
        IOptions<SettingEncryptionOptions>? encryption = null)
        => new DefaultSettingManager(Definitions(), store, Provider(store, dataProtection, encryption), [], WriteDependencies.Lock(), new RecordingUnitOfWorkManager(), dataProtectionProvider: dataProtection, encryptionOptions: encryption);

    private static DefaultSettingProvider Provider(
        FakeSettingStore store,
        IDataProtectionProvider? dataProtection,
        IOptions<SettingEncryptionOptions>? encryption = null)
        => new(Definitions(), store, new FakeCurrentUser(UserId), dataProtection, encryptionOptions: encryption);

    // 文档里的写法：同一密钥目录、同一应用名的两个实例即两个宿主共用的专用密钥环
    private static IDataProtectionProvider Dedicated(DirectoryInfo keys)
        => DataProtectionProvider.Create(keys, dataProtection => dataProtection.SetApplicationName("Tests.Settings"));

    private static IOptions<SettingEncryptionOptions> Encryption(IDataProtectionProvider dedicated)
        => Microsoft.Extensions.Options.Options.Create(new SettingEncryptionOptions { DataProtectionProvider = dedicated });

    private static SettingDefinitionManager Definitions() => new([new EncryptionTestDefinitionProvider()]);

    private sealed class EncryptionTestDefinitionProvider : ISettingDefinitionProvider
    {
        public void Define(ISettingDefinitionContext context)
        {
            var password = context.Add("Email.Password", scopes: SettingScopes.Host);
            password.IsEncrypted = true;
            password.IsVisibleToClients = true;

            context.Add("Api.Token", "fallback", SettingScopes.Tenant).IsEncrypted = true;
            context.Add("Email.Host", scopes: SettingScopes.Host).IsVisibleToClients = true;
        }
    }
}
