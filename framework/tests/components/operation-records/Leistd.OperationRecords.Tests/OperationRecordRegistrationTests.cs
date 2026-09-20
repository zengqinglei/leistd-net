using Leistd.OperationRecords.Abstractions;
using Leistd.OperationRecords.EntityFrameworkCore;
using Leistd.OperationRecords.EntityFrameworkCore.Stores;
using Leistd.OperationRecords.Options;
using Leistd.OperationRecords.Services;
using Leistd.OperationRecords.Tests.TestDoubles;
using Leistd.Security;
using Leistd.Timing;
using Leistd.Tracing;
using Leistd.UnitOfWork;
using Leistd.UnitOfWork.EntityFrameworkCore;
using Leistd.MultiTenancy;
using Microsoft.Extensions.Configuration;
using Leistd.TestBase.Assertions;
using Leistd.OperationRecords.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Leistd.OperationRecords.Tests;

/// <summary>
/// 注册面。
/// </summary>
/// <remarks>
/// Leistd 靠宿主显式调用 <c>AddXxx()</c> 组合，注册结果就是公共契约的一部分：
/// 生命周期写错、重复注册、组件之间意外互相覆盖，编译期一个都发现不了。
/// </remarks>
public sealed class OperationRecordRegistrationTests
{
    // ---------- Leistd.OperationRecords.Core 的 DependencyInjection ----------

    [Fact]
    public void AddOperationRecords_registers_the_recorder_as_transient()
    {
        var services = new ServiceCollection();

        services.AddOperationRecords();

        services.AssertSingle<IOperationRecorder>(ServiceLifetime.Transient);
        services.AssertImplementedBy<IOperationRecorder, OperationRecorder>();
    }

    [Fact]
    public void AddOperationRecords_is_idempotent()
    {
        // 宿主重复调用是常态（组合根拆分、EF 包内部也会调一次）。
        // 不幂等的表现是 IOperationRecorder 被解析成 IEnumerable 时出现重复项，同一件事记两遍。
        ServiceCollectionAssertions.AssertIdempotent(services => services.AddOperationRecords());
    }

    /// <summary>宿主不配置时选项也解析得出，值为默认 claim 名。</summary>
    /// <remarks>
    /// 漏了这一步的症状很隐蔽：记录器在解析 <c>IOptions&lt;T&gt;</c> 时才失败，
    /// 而那是第一次真的有人被拒之后——审计恰好在最需要它的时候不可用。
    /// </remarks>
    [Fact]
    public void The_options_resolve_with_defaults_without_any_configuration()
    {
        using var provider = new ServiceCollection().AddOperationRecords().BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<OperationRecordOptions>>().Value;

        Assert.Equal("impersonator_id", options.ImpersonatorIdClaimType);
        Assert.Equal("impersonator_name", options.ImpersonatorNameClaimType);
    }

    /// <summary>宿主签发的 claim 换了名字时从选项改，组件不写死。</summary>
    [Fact]
    public void The_configure_overload_overrides_the_claim_types()
    {
        using var provider = new ServiceCollection()
            .AddOperationRecords(options => options.ImpersonatorIdClaimType = "act_sub")
            .BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<OperationRecordOptions>>().Value;

        Assert.Equal("act_sub", options.ImpersonatorIdClaimType);
        // 只改了一项，另一项必须保持默认，而不是被整体覆盖成空
        Assert.Equal("impersonator_name", options.ImpersonatorNameClaimType);
    }

    /// <summary>Core 不替宿主注册持久化：缺存储时解析记录器直接失败，而不是静默什么都不记。</summary>
    [Fact]
    public void AddOperationRecords_does_not_register_a_store()
    {
        var services = new ServiceCollection();

        services.AddOperationRecords();

        services.AssertNotRegistered<IOperationRecordStore>();
    }

    // ---------- Leistd.OperationRecords.EntityFrameworkCore 的 DependencyInjection ----------

    [Fact]
    public void AddOperationRecordsEfCore_registers_the_store_as_transient()
    {
        var services = new ServiceCollection();

        services.AddOperationRecordsEfCore<TestDbContext>();

        services.AssertSingle<IOperationRecordStore>(ServiceLifetime.Transient);
        services.AssertImplementedBy<IOperationRecordStore, EfCoreOperationRecordStore<TestDbContext>>();
    }

    /// <summary>EF 入口内部已调用 Core 入口，宿主不必也调一次。</summary>
    [Fact]
    public void AddOperationRecordsEfCore_also_brings_the_recorder()
    {
        var services = new ServiceCollection();

        services.AddOperationRecordsEfCore<TestDbContext>();

        services.AssertSingle<IOperationRecorder>(ServiceLifetime.Transient);
    }

    [Fact]
    public void AddOperationRecordsEfCore_is_idempotent()
    {
        ServiceCollectionAssertions.AssertIdempotent(
            services => services.AddOperationRecordsEfCore<TestDbContext>());
    }

    /// <summary>记录器解析得出来，要宿主先组合四个独立组件。</summary>
    /// <remarks>
    /// <see cref="IOperationRecorder"/> 要回答"谁、在哪个租户、哪条链路、什么时间"，四样分别来自
    /// <c>AddAmbientContext()</c>、<c>AddMultiTenancyCore()</c>、<c>AddCorrelationIdCore(configuration)</c>
    /// 与宿主注册的 <see cref="IClock"/>。本组件一个都不替调用方注册，因此"注册面齐不齐"
    /// 编译期看不出来，只在首次解析时失败。
    /// </remarks>
    [Fact]
    public void The_recorder_resolves_once_its_cross_component_prerequisites_are_registered()
    {
        var configuration = new ConfigurationBuilder().Build();
        using var provider = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IConfiguration>(configuration)
            .AddUnitOfWork()
            .AddUnitOfWorkEfCore()
            .AddDbContext<TestDbContext>(options => options.UseSqlite("DataSource=:memory:"))
            .AddSingleton<IClock, UtcClockProvider>()
            .AddMultiTenancyCore()
            .AddAmbientContext()
            .AddCorrelationIdCore(configuration)
            .AddOperationRecordsEfCore<TestDbContext>()
            .BuildServiceProvider();

        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IOperationRecorder>());
    }

    /// <summary>
    /// 第二个 DbContext 在注册期就被拒
    /// </summary>
    /// <remarks>
    /// 静默取最后一条会让一半的审计写进宿主没预期的库——审计缺了一半比没有审计更危险，
    /// 因为查不到记录时人会以为"这件事没发生过"。
    /// </remarks>
    [Fact]
    public void A_second_authoritative_store_is_rejected_at_registration_time()
    {
        var services = new ServiceCollection();
        services.AddOperationRecordsEfCore<TestDbContext>();

        var exception = Assert.Throws<InvalidOperationException>(
            () => services.AddOperationRecordsEfCore<SecondDbContext>());

        Assert.Contains("single authoritative store", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// 可见性列有库默认值，但 EF 每次都显式写它。
    /// </summary>
    /// <remarks>
    /// 库默认值是给"宿主给存量表加这一列"的回填用的：没有它，历史行落空串、读取时枚举转换失败。
    /// 但 HasDefaultValue 会让 EF 在属性等于 CLR 默认值时省略该列，而 OperationVisibility.Tenant 正好是 0——
    /// 不配 ValueGeneratedNever，租户可见的记录会被库默认值静默写成宿主可见，那是越权。
    /// </remarks>
    [Fact]
    public void The_visibility_column_has_a_store_default_but_is_always_written()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        using var dbContext = new TestDbContext(options);

        var property = dbContext.Model
            .FindEntityType(typeof(OperationRecord))!
            .FindProperty(nameof(OperationRecord.Visibility))!;

        Assert.Equal(OperationVisibility.Host, property.GetDefaultValue());
        Assert.Equal(ValueGenerated.Never, property.ValueGenerated);
    }
}
