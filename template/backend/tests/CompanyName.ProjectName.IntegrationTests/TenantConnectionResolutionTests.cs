#if (!LocalIdentity)
using CompanyName.ProjectName.Infrastructure.TenantConnections;
using Leistd.Data.Abstractions;
using Leistd.ExceptionHandling;
using Leistd.MultiTenancy;
using Leistd.MultiTenancy.Abstractions;
using Leistd.MultiTenancy.ConnectionStrings;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CompanyName.ProjectName.IntegrationTests;

/// <summary>
/// Resource 宿主的连接解析：缓存必须按租户与连接名分区，单飞不得把取消传染给搭车者。
/// </summary>
/// <remarks>
/// <para>为什么是单元测试而不是走宿主：集成测试工厂会
/// <c>RemoveAll&lt;IConnectionStringResolver&gt;()</c> 换成 EF InMemory，
/// 生产解析链在集成测试里根本不执行。这里按生产的注册方式装配
/// （<c>AddRemoteTenantConnectionResolution</c> + 本服务的 Identity 适配器），只把 HTTP 客户端换成假件。</para>
/// </remarks>
public class TenantConnectionResolutionTests
{
    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TenantB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    /// <summary>
    /// 缓存键必须含租户 Id
    /// </summary>
    /// <remarks>
    /// 键漏掉租户 Id 时，先解析的租户会把自己的连接串留在缓存里，
    /// 后来的租户直接命中——读写都落到别人的库。这是数据泄露，不是性能问题。
    /// 断言方式刻意是行为而非键的字面量：键格式可以改，"两个租户拿到各自的库"不能改。
    /// </remarks>
    [Fact]
    public async Task Cache_is_partitioned_by_tenant()
    {
        var currentTenant = new FakeCurrentTenant();
        var client = new FakeConnectionClient();
        await using var provider = BuildHost(currentTenant, client);

        currentTenant.Id = TenantA;
        var first = await ResolveAsync(provider);

        currentTenant.Id = TenantB;
        var second = await ResolveAsync(provider);

        Assert.Equal("Host=tenant-aaaaaaaa", first);
        Assert.Equal("Host=tenant-bbbbbbbb", second);
        Assert.NotEqual(first, second);
    }

    /// <summary>同一租户第二次解析走缓存，不再打远端</summary>
    [Fact]
    public async Task Second_resolution_for_the_same_tenant_is_served_from_cache()
    {
        var currentTenant = new FakeCurrentTenant { Id = TenantA };
        var client = new FakeConnectionClient();
        await using var provider = BuildHost(currentTenant, client);

        await ResolveAsync(provider);
        await ResolveAsync(provider);

        Assert.Equal(1, client.CallCount);
    }

    /// <summary>
    /// 一条连接都没登记的租户用本服务自己的配置，且不再回源
    /// </summary>
    /// <remarks>
    /// 这是"不分库"的正常路径：去掉模式标志位之后，判据就是"该租户有没有登记过任何连接"。
    /// </remarks>
    [Fact]
    public async Task A_tenant_without_registrations_uses_the_host_connection()
    {
        var currentTenant = new FakeCurrentTenant { Id = TenantA };
        var client = new FakeConnectionClient { HasAnyConnection = false };
        await using var provider = BuildHost(currentTenant, client);

        Assert.Equal("Host=host-default", await ResolveAsync(provider));
    }

    /// <summary>
    /// 分库租户缺这个服务的连接时失败关闭，不回落到本服务的库
    /// </summary>
    /// <remarks>
    /// 本服务的库里没有它的数据，静默连过去就是把它的写入落进别人的库。
    /// </remarks>
    [Fact]
    public async Task A_registered_tenant_missing_this_name_is_refused()
    {
        var currentTenant = new FakeCurrentTenant { Id = TenantA };
        var client = new FakeConnectionClient { HasAnyConnection = true, ReturnConnection = false };
        await using var provider = BuildHost(currentTenant, client);

        await Assert.ThrowsAsync<InternalServerException>(() => ResolveAsync(provider));
    }

    /// <summary>
    /// 发起者取消不牵连搭车者
    /// </summary>
    /// <remarks>
    /// 单飞把远端调用合并成一个共享任务。若共享任务接的是发起者的
    /// <c>CancellationToken</c>，发起者断开连接（客户端关闭、网关超时）就会取消那个共享任务，
    /// 于是明明还活着的搭车者一起收到 <c>OperationCanceledException</c>——
    /// 一个用户按下停止，另一个用户的请求 500。
    /// </remarks>
    [Fact]
    public async Task Cancelling_the_first_caller_does_not_cancel_a_joined_caller()
    {
        var currentTenant = new FakeCurrentTenant { Id = TenantA };
        var client = new FakeConnectionClient { BlockUntilReleased = true };
        await using var provider = BuildHost(currentTenant, client);

        using var initiator = new CancellationTokenSource();
        Task<string> initiatorTask;
        Task<string> joinerTask;

        try
        {
            initiatorTask = ResolveAsync(provider, initiator.Token);

            // 等到远端调用确实在飞，再让第二个调用方搭车
            await client.Started.Task;
            joinerTask = ResolveAsync(provider, CancellationToken.None);

            await initiator.CancelAsync();

            // 发起者必须及时以取消收场。带缺陷的版本里它会继续等共享任务完成，
            // 于是这里超时——而不是让整个测试进程挂死等到 CI 超时
            Assert.Same(
                initiatorTask,
                await Task.WhenAny(initiatorTask, Task.Delay(TimeSpan.FromSeconds(5))));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => initiatorTask);
        }
        finally
        {
            // 必须无条件释放，留下一个未完成的共享任务会挂住容器释放
            client.Release();
        }

        // 搭车者必须照常拿到结果：共享任务不该被别人的令牌掐断
        Assert.Equal("Host=tenant-aaaaaaaa", await joinerTask.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(1, client.CallCount);
    }

    /// <summary>
    /// 远端返回的结果不属于所问的租户时必须失败关闭
    /// </summary>
    /// <remarks>
    /// 不校验的话，B 租户的库目标会被写进以 A 租户为键的缓存，此后 A 的每个请求都命中它——
    /// 读写都落到别人的库上，且不报任何错。串租户不必来自攻击：Identity 侧的缓存、分页或
    /// 序列化缺陷都能产生它。校验必须在使用连接串与写缓存之前，否则错误路由已经落库。
    /// </remarks>
    [Fact]
    public async Task Remote_result_for_another_tenant_is_rejected()
    {
        var currentTenant = new FakeCurrentTenant { Id = TenantA };
        var client = new FakeConnectionClient { RespondWithTenantId = TenantB };
        await using var provider = BuildHost(currentTenant, client);

        var error = await Assert.ThrowsAsync<InternalServerException>(() => ResolveAsync(provider));
        Assert.Contains(TenantA.ToString(), error.Message, StringComparison.Ordinal);
        Assert.Contains(TenantB.ToString(), error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Host=", error.Message, StringComparison.Ordinal);

        // 失败之后不得留下缓存条目：留下的话下一次请求会命中错误路由而不再触发校验
        await Assert.ThrowsAsync<InternalServerException>(() => ResolveAsync(provider));
        Assert.Equal(2, client.CallCount);
    }

    /// <summary>
    /// 发起者的作用域在共享任务还在飞的时候被释放，搭车者仍然拿到结果。
    /// </summary>
    /// <remarks>
    /// 共享任务被刻意设计成比发起者活得更久（发起者取消不取消它）。既然如此，
    /// 它就不能持有发起者请求作用域里的实例——那个作用域随时可能先释放，
    /// 表现是负载一上来偶发 <c>ObjectDisposedException</c>，几乎无法定位。
    /// 本用例用"释放后即不可用"的 scoped 假件把这条从注释变成断言。
    /// </remarks>
    [Fact]
    public async Task Shared_resolution_survives_disposal_of_the_initiating_scope()
    {
        var currentTenant = new FakeCurrentTenant { Id = TenantA };
        var client = new FakeConnectionClient { BlockUntilReleased = true };
        await using var provider = BuildHost(currentTenant, client);

        Task<string> initiatorTask;
        Task<string> joinerTask;

        var initiatorScope = provider.CreateScope();
        try
        {
            initiatorTask = initiatorScope.ServiceProvider
                .GetRequiredService<IConnectionStringResolver>()
                .ResolveAsync("Default");

            await client.Started.Task;

            // 发起者的作用域先走了——请求结束、客户端断开都会这样
            initiatorScope.Dispose();

            using var joinerScope = provider.CreateScope();
            joinerTask = joinerScope.ServiceProvider
                .GetRequiredService<IConnectionStringResolver>()
                .ResolveAsync("Default");
        }
        finally
        {
            client.Release();
        }

        Assert.Equal("Host=tenant-aaaaaaaa", await initiatorTask.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal("Host=tenant-aaaaaaaa", await joinerTask.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(1, client.CallCount);
    }

    private static async Task<string> ResolveAsync(ServiceProvider provider, CancellationToken cancellationToken = default)
    {
        using var scope = provider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IConnectionStringResolver>()
            .ResolveAsync("Default", cancellationToken);
    }

    /// <summary>
    /// 按生产注册方式装配；远端依赖注册为 <b>Scoped</b> 且"释放后不可用"，
    /// 这样"共享任务用了谁的作用域"就变成可断言的事实而不是注释里的承诺。
    /// </summary>
    private static ServiceProvider BuildHost(ICurrentTenant currentTenant, FakeConnectionClient client)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = "Host=host-default",
                ["TenantRouting:CacheLifetime"] = "00:10:00"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton(currentTenant);
        services.AddRemoteTenantConnectionResolution();
        services.AddScoped<IIdentityTenantConnectionClient>(_ => new ScopeBoundClient(client));
        services.AddScoped<ITenantConnectionConfigurationStore, IdentityTenantConnectionStore>();

        return services.BuildServiceProvider();
    }

    /// <summary>释放后即不可用的 scoped 包装：证明共享任务没有用调用方的作用域</summary>
    private sealed class ScopeBoundClient(FakeConnectionClient inner) : IIdentityTenantConnectionClient, IDisposable
    {
        private bool _disposed;

        public void Dispose() => _disposed = true;

        public Task<RemoteTenantConnectionLookup> GetRuntimeAsync(
            Guid tenantId,
            string name,
            CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return inner.GetRuntimeAsync(tenantId, name, cancellationToken);
        }

        public Task<IReadOnlyList<RemoteTenantMigrationConnection>> GetMigrationListAsync(
            string name,
            CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return inner.GetMigrationListAsync(name, cancellationToken);
        }
    }

    private sealed class FakeCurrentTenant : ICurrentTenant
    {
        public Guid? Id { get; set; }
        public string? Name => null;
        public bool IsAvailable => Id.HasValue;
        public IDisposable Change(Guid? id, string? name = null) => throw new NotSupportedException();
    }

    private sealed class FakeConnectionClient : IIdentityTenantConnectionClient
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool BlockUntilReleased { get; init; }

        /// <summary>该租户是否登记过任何连接；false 即"不分库"</summary>
        public bool HasAnyConnection { get; init; } = true;

        /// <summary>false 表示登记过但没有这个名字，也没有默认名可回落</summary>
        public bool ReturnConnection { get; init; } = true;

        /// <summary>非空时，无论问的是哪个租户都返回这个租户的结果（模拟 Identity 侧串租户）</summary>
        public Guid? RespondWithTenantId { get; init; }

        public int CallCount { get; private set; }

        public void Release() => _release.TrySetResult();

        public async Task<RemoteTenantConnectionLookup> GetRuntimeAsync(
            Guid tenantId,
            string name,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            Started.TrySetResult();

            if (BlockUntilReleased)
            {
                // 刻意不观察 cancellationToken：本替身模拟"远端调用已经发出、
                // 只等响应"的状态。若被测代码把调用方的令牌传进来，
                // 取消会在 await 边界之外生效，这个用例就抓不到问题了
                await _release.Task;
            }

            return new RemoteTenantConnectionLookup
            {
                TenantId = RespondWithTenantId ?? tenantId,
                HasAnyConnection = HasAnyConnection,
                Connection = HasAnyConnection && ReturnConnection
                    ? new RemoteTenantConnection
                    {
                        Name = name,
                        ConnectionString = $"Host=tenant-{tenantId.ToString()[..8]}",
                        Version = 1
                    }
                    : null
            };
        }

        public Task<IReadOnlyList<RemoteTenantMigrationConnection>> GetMigrationListAsync(
            string name,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
#endif
