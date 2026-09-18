using Leistd.MultiTenancy.Abstractions;
using Leistd.OperationRecords.Abstractions;
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
    bool tracksChanges = false) : IOperationActionDefinition
{
    public string Code { get; } = code;
    public string Category { get; } = category;
    public OperationVisibility Visibility { get; } = visibility;
    public OperationSeverity Severity { get; } = severity;
    public bool TracksChanges { get; } = tracksChanges;
}

/// <summary>
/// 按给定的"动作码 → 可见性"表作答；表里没有的一律当作<b>未登记</b>。
/// </summary>
/// <remarks>
/// 默认构造是空表，因此不关心可见性的用例照常走"未登记"分支，
/// 记录器据此盖上最严格的 <see cref="OperationVisibility.Host"/>。
/// </remarks>
internal sealed class FakeOperationActionDefinitionManager(
    IReadOnlyDictionary<string, OperationVisibility>? registered = null) : IOperationActionDefinitionManager
{
    private readonly IReadOnlyDictionary<string, OperationVisibility> _registered =
        registered ?? new Dictionary<string, OperationVisibility>(StringComparer.Ordinal);

    public IOperationActionDefinition? GetOrNull(string code)
        => _registered.TryGetValue(code, out var visibility)
            ? new FakeOperationActionDefinition(code, visibility)
            : null;

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

    public Task InsertAsync(OperationRecordInfo record, CancellationToken cancellationToken = default)
    {
        Written.Add(record);
        return Task.CompletedTask;
    }

    public Task<OperationRecordPage> GetPagedListAsync(
        string? keyword, DateTime? startTime, DateTime? endTime,
        int skip, int take, OperationRecordVisibilityScope scope = default,
        IReadOnlyCollection<string>? actions = null,
        OperationRecordOutcome? outcome = null,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException();
}

/// <summary>写入必定失败的存储，用于区分"吞掉"与"上抛"两条策略。</summary>
internal sealed class ThrowingOperationRecordStore(Exception failure) : IOperationRecordStore
{
    public Task InsertAsync(OperationRecordInfo record, CancellationToken cancellationToken = default)
        => throw failure;

    public Task<OperationRecordPage> GetPagedListAsync(
        string? keyword, DateTime? startTime, DateTime? endTime,
        int skip, int take, OperationRecordVisibilityScope scope = default,
        IReadOnlyCollection<string>? actions = null,
        OperationRecordOutcome? outcome = null,
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
