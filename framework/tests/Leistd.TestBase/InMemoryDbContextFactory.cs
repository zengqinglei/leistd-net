using Microsoft.EntityFrameworkCore;

namespace Leistd.TestBase;

/// <summary>
/// 为测试创建隔离的 EF Core InMemory <see cref="DbContext"/>。
/// 每次调用用唯一库名，避免测试间共享状态（沿用现有测试项目的约定）。
/// 注意：InMemory provider 不支持事务；涉及事务分支的测试请改用 SQLite in-memory。
/// </summary>
public static class InMemoryDbContextFactory
{
    /// <summary>
    /// 用唯一库名构造 <typeparamref name="TContext"/>。
    /// </summary>
    /// <param name="databaseName">可选库名前缀；缺省用类型名。始终追加唯一后缀。</param>
    public static TContext Create<TContext>(string? databaseName = null)
        where TContext : DbContext
    {
        var options = new DbContextOptionsBuilder<TContext>()
            .UseInMemoryDatabase($"{databaseName ?? typeof(TContext).Name}-{Guid.NewGuid():N}")
            .Options;

        return (TContext)Activator.CreateInstance(typeof(TContext), options)!;
    }

    /// <summary>
    /// 用调用方提供的工厂构造（当 <typeparamref name="TContext"/> 的构造函数签名非
    /// 单一 <see cref="DbContextOptions{TContext}"/> 时使用）。
    /// </summary>
    public static TContext Create<TContext>(
        Func<DbContextOptions<TContext>, TContext> factory,
        string? databaseName = null)
        where TContext : DbContext
    {
        var options = new DbContextOptionsBuilder<TContext>()
            .UseInMemoryDatabase($"{databaseName ?? typeof(TContext).Name}-{Guid.NewGuid():N}")
            .Options;

        return factory(options);
    }
}
