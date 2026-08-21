using System.Security.Claims;
using Leistd.Auditing;
using Leistd.Auditing.EntityFrameworkCore;
using Leistd.Ddd.Domain.Entities;
using Leistd.Ddd.Infrastructure.Persistence;
using Leistd.Security.Claims;
using Leistd.Security.Users;
using Leistd.Timing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Leistd.Ddd.Infrastructure.Tests;

/// <summary>
/// 创建审计的落值时机：必须在实体**进入跟踪**时，不是保存时。
/// </summary>
/// <remarks>
/// <para>锁死的缺陷：创建审计原先在 <c>AuditSaveChangesInterceptor.SavingChanges</c> 里落值。
/// 仓储在工作单元内不立即保存，新增与保存之间可以跨越
/// <c>ICurrentPrincipalAccessor.Change</c> 的边界——于是 <c>CreatorId</c> 落成外层主体，
/// 不报错。这与本轮修掉的 <c>TenantId</c> 是同一个缺陷形状，只是字段不同。</para>
/// <para>另一半同等重要：提早落值**不能**碰查询出来的实体。三层护栏
/// （<c>FromQuery</c>、<c>State == Added</c>、值已有则不动）各有一条用例。</para>
/// </remarks>
public class CreationAuditTimingTests
{
    private static readonly Guid ScopeUser = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OuterUser = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void Creation_audit_lands_when_the_entity_enters_tracking_not_at_save()
    {
        var (db, accessor, sp) = Build();
        using var _ = sp;
        using var __ = db;

        using (accessor.Change(PrincipalOf(ScopeUser)))
        {
            db.Items.Add(new TestEntity("tracked-in-scope"));
        }

        // 还没有 SaveChanges：值必须已经在了。落在保存时的话这里是 null，
        // 保存前读它的代码（领域事件、校验、导出）看到的就是空
        var entity = db.ChangeTracker.Entries<TestEntity>().Single().Entity;
        Assert.Equal(ScopeUser.ToString(), entity.CreatorId);
        Assert.NotEqual(default, entity.CreationTime);
    }

    [Fact]
    public async Task Entity_added_inside_a_principal_scope_keeps_that_creator_when_saved_later()
    {
        var (db, accessor, sp) = Build();
        using var _ = sp;
        using var __ = db;

        using (accessor.Change(PrincipalOf(ScopeUser)))
        {
            db.Items.Add(new TestEntity("added-in-scope"));
        }

        // 作用域已退出，改由外层主体提交——模拟 UoW 统一提交
        using (accessor.Change(PrincipalOf(OuterUser)))
        {
            await db.SaveChangesAsync();
        }

        var saved = await db.Items.SingleAsync();
        Assert.Equal(ScopeUser.ToString(), saved.CreatorId);
    }

    [Fact]
    public async Task Creation_audit_of_a_queried_entity_is_not_overwritten()
    {
        var (db, accessor, sp) = Build(out var options);
        using var _ = sp;

        using (accessor.Change(PrincipalOf(ScopeUser)))
        {
            db.Items.Add(new TestEntity("created-once"));
            await db.SaveChangesAsync();
        }

        await db.DisposeAsync();

        // 换一个上下文、换一个主体重新查出来：查询物化不得触发落值
        using var reread = new TestDbContext(options, sp);
        TestEntity queried;
        using (accessor.Change(PrincipalOf(OuterUser)))
        {
            queried = await reread.Items.SingleAsync();
            queried.Rename("renamed-by-outer");
            await reread.SaveChangesAsync();
        }

        Assert.Equal(ScopeUser.ToString(), queried.CreatorId);
        Assert.Equal(OuterUser.ToString(), queried.LastModifierId);
    }

    [Fact]
    public void Explicitly_assigned_creation_audit_is_not_overwritten()
    {
        var (db, accessor, sp) = Build();
        using var _ = sp;
        using var __ = db;

        var seeded = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var entity = new TestEntity("seeded");
        entity.AssignCreationAudit(OuterUser.ToString(), seeded);

        using (accessor.Change(PrincipalOf(ScopeUser)))
        {
            db.Items.Add(entity);
        }

        // 种子/导入/迁移显式赋过的值不动
        Assert.Equal(OuterUser.ToString(), entity.CreatorId);
        Assert.Equal(seeded, entity.CreationTime);
    }

    [Fact]
    public void Entity_switched_to_added_after_tracking_still_gets_creation_audit()
    {
        var (db, accessor, sp) = Build();
        using var _ = sp;
        using var __ = db;

        var entity = new TestEntity("attached-then-added");
        db.Items.Attach(entity);                 // 以 Unchanged 进入跟踪，不触发落值
        Assert.Null(entity.CreatorId);

        using (accessor.Change(PrincipalOf(ScopeUser)))
        {
            // upsert 类写法：跟踪后才改成 Added。Tracked 事件已经触发完了，
            // 只有 StateChanged 能看到这次迁移——只订阅 Tracked 会在此漏值
            db.Entry(entity).State = EntityState.Added;
        }

        Assert.Equal(ScopeUser.ToString(), entity.CreatorId);
    }

    #region 装配

    private static (TestDbContext db, ICurrentPrincipalAccessor accessor, ServiceProvider sp) Build() =>
        Build(out _);

    private static (TestDbContext db, ICurrentPrincipalAccessor accessor, ServiceProvider sp) Build(
        out DbContextOptions options)
    {
        var sp = new ServiceCollection()
            .AddSingleton<IClock, UtcClockProvider>()
            .AddSingleton<ICurrentPrincipalAccessor, TestCurrentPrincipalAccessor>()
            .AddSingleton<ICurrentUser, CurrentUser>()
            .AddAuditingEfCore()
            .BuildServiceProvider();

        // 修改/删除审计仍在保存时刻，拦截器照常挂载——本测试也顺带锁住这条分界线
        options = new DbContextOptionsBuilder()
            .UseInMemoryDatabase($"audit-{Guid.NewGuid()}")
            .AddInterceptors(sp.GetRequiredService<AuditSaveChangesInterceptor>())
            .Options;

        return (new TestDbContext(options, sp), sp.GetRequiredService<ICurrentPrincipalAccessor>(), sp);
    }

    private static ClaimsPrincipal PrincipalOf(Guid userId) =>
        new(new ClaimsIdentity([new Claim("sub", userId.ToString())], "Test"));

    private sealed class TestCurrentPrincipalAccessor : CurrentPrincipalAccessor
    {
        protected override ClaimsPrincipal? GetClaimsPrincipal() => null;
    }

    private sealed class TestEntity : Entity<Guid>, ICreationAuditedObject, IModificationAuditedObject
    {
        public string Name { get; private set; } = "";
        public DateTime CreationTime { get; private set; }
        public string? CreatorId { get; private set; }
        public DateTime? LastModificationTime { get; private set; }
        public string? LastModifierId { get; private set; }

        private TestEntity() { }

        public TestEntity(string name)
        {
            Id = Guid.NewGuid();
            Name = name;
        }

        public void Rename(string name) => Name = name;

        public void AssignCreationAudit(string creatorId, DateTime creationTime)
        {
            CreatorId = creatorId;
            CreationTime = creationTime;
        }
    }

    private sealed class TestDbContext(DbContextOptions options, IServiceProvider serviceProvider)
        : BaseDbContext(options, serviceProvider)
    {
        public DbSet<TestEntity> Items => Set<TestEntity>();

        protected override void ConfigureModel(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<TestEntity>(b =>
            {
                b.HasKey(x => x.Id);
                b.Property(x => x.Name);
                b.Property(x => x.CreationTime);
                b.Property(x => x.CreatorId);
                b.Property(x => x.LastModificationTime);
                b.Property(x => x.LastModifierId);
            });
        }
    }

    #endregion
}
