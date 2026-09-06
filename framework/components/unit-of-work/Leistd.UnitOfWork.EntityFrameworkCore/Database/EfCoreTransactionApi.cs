using Leistd.UnitOfWork.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Leistd.UnitOfWork.EntityFrameworkCore.Database;

/// <summary>
/// 把一个 EF Core 事务挂到工作单元上，并在提交/回滚时带上加入该事务的其它上下文。
/// </summary>
/// <remarks>
/// 事务由提供方为本工作单元开启，所有权因此在工作单元这边——本类实现
/// <see cref="IDisposable"/>，工作单元释放时一并释放事务。与
/// <see cref="EfCoreDatabaseApi{TDbContext}"/> 的差别见 <see cref="IDatabaseApi"/>。
/// </remarks>
public class EfCoreTransactionApi(IDbContextTransaction dbContextTransaction, DbContext starterDbContext)
    : ITransactionApi, ISupportsRollback
{
    /// <summary>底层 EF Core 事务。</summary>
    public IDbContextTransaction DbContextTransaction { get; } = dbContextTransaction;

    /// <summary>开启本事务的上下文；提交与回滚由它执行。</summary>
    public DbContext StarterDbContext { get; } = starterDbContext;

    /// <summary>加入本事务的其它上下文，提交前一并 <c>SaveChanges</c>。</summary>
    public List<DbContext> AttendedDbContexts { get; } = new();

    /// <inheritdoc />
    public async Task CommitAsync()
    {
        // 先处理所有 AttendedDbContexts
        foreach (var dbContext in AttendedDbContexts)
        {
            // 关系型数据库且共享同一连接时，跳过（会随主事务一起提交）
            if (dbContext.HasRelationalTransactionManager() &&
                dbContext.Database.GetDbConnection() == DbContextTransaction.GetDbTransaction().Connection)
            {
                continue;
            }

            // 非关系型数据库或使用不同连接的数据库，需要单独提交
            await dbContext.Database.CommitTransactionAsync();
        }

        // 最后提交主事务
        await DbContextTransaction.CommitAsync();
    }

    /// <inheritdoc />
    public async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        // 先处理所有 AttendedDbContexts
        foreach (var dbContext in AttendedDbContexts)
        {
            // 关系型数据库且共享同一连接时，跳过（会随主事务一起回滚）
            if (dbContext.HasRelationalTransactionManager() &&
                dbContext.Database.GetDbConnection() == DbContextTransaction.GetDbTransaction().Connection)
            {
                continue;
            }

            // 非关系型数据库或使用不同连接的数据库，需要单独回滚
            await dbContext.Database.RollbackTransactionAsync(cancellationToken);
        }

        // 最后回滚主事务
        await DbContextTransaction.RollbackAsync(cancellationToken);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        DbContextTransaction.Dispose();
    }
}
