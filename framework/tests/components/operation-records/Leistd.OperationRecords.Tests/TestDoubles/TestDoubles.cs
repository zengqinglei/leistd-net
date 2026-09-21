using Leistd.Data.Paging;
using Leistd.MultiTenancy.ConnectionStrings;
using Leistd.MultiTenancy.Context;
using Leistd.MultiTenancy.Errors;
using Leistd.MultiTenancy.Management;
using Leistd.MultiTenancy.Tenancy;
using Leistd.OperationRecords.Definitions;
using Leistd.OperationRecords.Models;
using Leistd.OperationRecords.Queries;
using Leistd.OperationRecords.Recording;
using Leistd.OperationRecords.Stores;
using Leistd.OperationRecords.EntityFrameworkCore;
using Leistd.OperationRecords.EntityFrameworkCore.Entities;
using Leistd.Tracing.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Leistd.OperationRecords.Tests.TestDoubles;

/// <summary>固定租户上下文。</summary>
internal sealed class FakeCurrentTenant(Guid? id) : ICurrentTenant
{
    public bool IsAvailable => Id.HasValue;
    public Guid? Id { get; } = id;
    public string? Name => null;
    public IDisposable Change(Guid? id, string? name = null) => throw new NotSupportedException();
}

/// <summary>一条最小的动作定义，只承载断言要用到的字段。</summary>
internal sealed class FakeOperationActionDefinition(
    string code,
    OperationVisibility visibility,
    string category = "test",
    OperationSeverity severity = OperationSeverity.Info,
    bool targetIsActor = false) : IOperationActionDefinition
{
    public string Code { get; } = code;
    public string Category { get; } = category;
    public OperationVisibility Visibility { get; } = visibility;
    public OperationSeverity Severity { get; } = severity;
    public bool TargetIsActor { get; } = targetIsActor;
}

/// <summary>
/// 按给定的"动作码 → 可见性"表作答；表里没有的码按 <paramref name="otherCodes"/> 作答。
/// </summary>
/// <remarks>
/// 默认把任何码都当作已登记的 <see cref="OperationVisibility.Tenant"/>，不关心可见性的用例因此不必逐个登记；
/// 验证"未登记"的用例显式传 <c>otherCodes: null</c>。
/// </remarks>
internal sealed class FakeOperationActionDefinitionManager(
    IReadOnlyDictionary<string, OperationVisibility>? registered = null,
    OperationVisibility? otherCodes = OperationVisibility.Tenant) : IOperationActionDefinitionManager
{
    private readonly IReadOnlyDictionary<string, OperationVisibility> _registered =
        registered ?? new Dictionary<string, OperationVisibility>(StringComparer.Ordinal);

    public IOperationActionDefinition? GetOrNull(string code)
        => _registered.TryGetValue(code, out var visibility)
            ? new FakeOperationActionDefinition(code, visibility)
            : otherCodes is { } fallback ? new FakeOperationActionDefinition(code, fallback) : null;

    public IReadOnlyList<IOperationActionDefinition> GetAll()
        => [.. _registered.Select(pair => new FakeOperationActionDefinition(pair.Key, pair.Value))];

    public IReadOnlyList<string> GetCategories() => _registered.Count == 0 ? [] : ["test"];
}

/// <summary>固定链路标识。</summary>
internal sealed class FakeCorrelationIdProvider(string? correlationId) : ICorrelationIdProvider
{
    public string? Get() => correlationId;
    public string Create() => throw new NotSupportedException();
    public IDisposable Change(string correlationId) => throw new NotSupportedException();
}

/// <summary>把写入原样收下的存储，供断言"记了什么"。</summary>
internal sealed class RecordingOperationRecordStore : IOperationRecordStore
{
    public List<OperationRecordInfo> Written { get; } = [];

    /// <summary>最近一次查询收到的筛选条件与分页。</summary>
    public (OperationRecordFilter Filter, PageRequest Page)? LastQuery { get; private set; }

    public Task InsertAsync(OperationRecordInfo record, CancellationToken cancellationToken = default)
    {
        Written.Add(record);
        return Task.CompletedTask;
    }

    // 不做筛选：查询用例的断言关心"交给存储的条件"与"拿回来之后怎么裁剪"，筛选语义由存储自己的用例钉住
    public Task<PagedResult<OperationRecordInfo>> GetPagedListAsync(
        OperationRecordFilter filter,
        PageRequest page,
        CancellationToken cancellationToken = default)
    {
        LastQuery = (filter, page);
        return Task.FromResult(new PagedResult<OperationRecordInfo>(Written.Count, Written.Skip(page.Offset).Take(page.Limit)));
    }
}

/// <summary>写入必定失败的存储，用于区分"吞掉"与"上抛"两条策略。</summary>
internal sealed class ThrowingOperationRecordStore(Exception failure) : IOperationRecordStore
{
    public Task InsertAsync(OperationRecordInfo record, CancellationToken cancellationToken = default)
        => throw failure;

    public Task<PagedResult<OperationRecordInfo>> GetPagedListAsync(
        OperationRecordFilter filter,
        PageRequest page,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException();
}

/// <summary>承载操作记录表的测试上下文。</summary>
/// <remarks>表名跟随本 <c>DbSet</c> 属性名，用于钉住"组件不写死表名"。</remarks>
internal sealed class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options)
{
    public DbSet<OperationRecord> OperationRecords => Set<OperationRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.ConfigureOperationRecords();
}

/// <summary>映射同一张表的第二个上下文，用于验证"只能有一个权威存储"。</summary>
internal sealed class SecondDbContext(DbContextOptions<SecondDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.ConfigureOperationRecords();
}
