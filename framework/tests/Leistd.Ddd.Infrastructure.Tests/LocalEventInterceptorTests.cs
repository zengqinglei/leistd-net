using Leistd.Ddd.Domain.Entities;
using Leistd.Ddd.Infrastructure.EventBus;
using Leistd.Ddd.Infrastructure.Persistence;
using Leistd.EventBus.Core.Event;
using Leistd.EventBus.Core.EventBus;
using Leistd.EventBus.Core.EventHandler;
using Leistd.EventBus.Local;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Leistd.Ddd.Infrastructure.Tests;

/// <summary>
/// 回归测试：领域对象 AddLocalEvent → 仓储/DbContext SaveChanges → 本地事件被发布到 handler。
/// </summary>
/// <remarks>
/// 锁死“收集时机”bug：曾在 SavedChanges（保存后实体已 Unchanged）收集，导致收集到 0 个事件、handler 不触发。
/// 此测试走真实 SaveChanges 拦截器链路（非直接 PublishAsync），覆盖业务真实路径。
/// </remarks>
public class LocalEventInterceptorTests
{
    private sealed class TestEntity : Entity<Guid>
    {
        public string Name { get; private set; } = "";
        private TestEntity() { }
        public TestEntity(string name)
        {
            Id = Guid.NewGuid();
            Name = name;
            AddLocalEvent(new TestCreatedEvent(Name));
        }
    }

    private sealed class TestCreatedEvent(string name) : LocalEvent
    {
        public string Name { get; } = name;
    }

    private sealed class TestDbContext(DbContextOptions options) : BaseDbContext(options)
    {
        public DbSet<TestEntity> Items => Set<TestEntity>();
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<TestEntity>(b =>
            {
                b.HasKey(x => x.Id);
                b.Property(x => x.Name);
            });
        }
    }

    private sealed class TestHandler : IEventHandler<TestCreatedEvent>
    {
        public static int Invoked;
        public static string? LastName;
        public Task HandleAsync(TestCreatedEvent @event, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref Invoked);
            LastName = @event.Name;
            return Task.CompletedTask;
        }
    }

    private static (TestDbContext db, ServiceProvider sp) Build()
    {
        TestHandler.Invoked = 0;
        TestHandler.LastName = null;

        var sp = new ServiceCollection()
            .AddLocalEventBus()
            .AddScoped<IEventHandler<TestCreatedEvent>, TestHandler>()
            .BuildServiceProvider();

        var interceptor = new LocalEventSaveChangesInterceptor(
            sp.GetRequiredService<ILocalEventBus>(),
            unitOfWorkManager: null,
            NullLogger<LocalEventSaveChangesInterceptor>.Instance);

        var options = new DbContextOptionsBuilder()
            .UseInMemoryDatabase($"evt-{Guid.NewGuid()}")
            .AddInterceptors(interceptor)
            .Options;

        return (new TestDbContext(options), sp);
    }

    [Fact]
    public async Task SaveChangesAsync_publishes_local_events_to_handler()
    {
        var (db, sp) = Build();
        using var _ = sp;

        db.Items.Add(new TestEntity("created-via-savechanges"));
        await db.SaveChangesAsync();

        Assert.Equal(1, TestHandler.Invoked);
        Assert.Equal("created-via-savechanges", TestHandler.LastName);
    }

    [Fact]
    public void SaveChanges_sync_publishes_local_events_to_handler()
    {
        var (db, sp) = Build();
        using var _ = sp;

        db.Items.Add(new TestEntity("created-sync"));
        db.SaveChanges();

        Assert.Equal(1, TestHandler.Invoked);
    }
}
