using Leistd.Ddd.Domain.Entities;
using Leistd.Ddd.Domain.Repositories;
using Leistd.UnitOfWork;
using Leistd.UnitOfWork.EntityFrameworkCore.Database;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;

namespace Leistd.Ddd.Infrastructure.Persistence.Repositories;

/// <summary>
/// 使用 EF Core 访问无强类型主键的实体。
/// </summary>
/// <remarks>
/// 上下文经 <see cref="IDbContextProvider{TDbContext}"/> 取得，因此始终绑定当前工作单元已解析的连接；
/// 不在工作单元内时每个写方法各自保存，在工作单元内只登记变更、由工作单元统一提交。
/// </remarks>
/// <typeparam name="TDbContext">上下文类型。</typeparam>
/// <typeparam name="TEntity">实体类型。</typeparam>
public class EfCoreRepository<TDbContext, TEntity>(
    IDbContextProvider<TDbContext> dbContextProvider,
    IUnitOfWorkManager uow) : IRepository<TEntity>
    where TDbContext : DbContext
    where TEntity : class, IEntity
{
    /// <summary>上下文提供方，负责把上下文绑定到当前工作单元。</summary>
    protected readonly IDbContextProvider<TDbContext> DbContextProvider = dbContextProvider;
    /// <summary>工作单元管理器，用于判断当前是否处在工作单元内。</summary>
    protected readonly IUnitOfWorkManager Uow = uow;

    /// <summary>取当前工作单元绑定的上下文实例。</summary>
    protected async Task<TDbContext> GetDbContextAsync(CancellationToken cancellationToken = default)
    {
        return await DbContextProvider.GetDbContextAsync(cancellationToken);
    }

    /// <summary>取本实体的 <see cref="DbSet{TEntity}"/>。派生类可覆盖以追加 <c>Include</c> 等默认行为。</summary>
    protected virtual async Task<DbSet<TEntity>> GetDbSetAsync(CancellationToken cancellationToken = default)
    {
        var dbContext = await GetDbContextAsync(cancellationToken);
        return dbContext.Set<TEntity>();
    }

    /// <inheritdoc />
    public virtual async Task<IQueryable<TEntity>> GetQueryableAsync(CancellationToken cancellationToken = default)
    {
        var dbSet = await GetDbSetAsync(cancellationToken);
        return dbSet.AsQueryable();
    }

    /// <inheritdoc />
    public virtual async Task<TEntity?> GetFirstAsync(Expression<Func<TEntity, bool>> predicate, Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>>? orderBy = null, CancellationToken cancellationToken = default)
    {
        var dbSet = await GetDbSetAsync(cancellationToken);
        var query = dbSet.Where(predicate);
        return await (orderBy != null ? orderBy(query) : query).FirstOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    public virtual async Task<IEnumerable<TEntity>> GetListAsync(Expression<Func<TEntity, bool>>? predicate = null, CancellationToken cancellationToken = default)
    {
        var dbSet = await GetDbSetAsync(cancellationToken);
        return predicate == null
            ? await dbSet.ToListAsync(cancellationToken)
            : await dbSet.Where(predicate).ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public virtual async Task<TEntity?> GetOneAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
    {
        var dbSet = await GetDbSetAsync(cancellationToken);
        return await dbSet.SingleOrDefaultAsync(predicate, cancellationToken);
    }

    /// <inheritdoc />
    public virtual async Task<long> CountAsync(Expression<Func<TEntity, bool>>? predicate = null, CancellationToken cancellationToken = default)
    {
        var dbSet = await GetDbSetAsync(cancellationToken);
        return predicate == null
            ? await dbSet.LongCountAsync(cancellationToken)
            : await dbSet.LongCountAsync(predicate, cancellationToken);
    }

    /// <inheritdoc />
    public virtual async Task<bool> AnyAsync(Expression<Func<TEntity, bool>>? predicate = null, CancellationToken cancellationToken = default)
    {
        var dbSet = await GetDbSetAsync(cancellationToken);
        return predicate == null
            ? await dbSet.AnyAsync(cancellationToken)
            : await dbSet.AnyAsync(predicate, cancellationToken);
    }

    /// <inheritdoc />
    public virtual async Task<TEntity> InsertAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        var dbSet = await GetDbSetAsync(cancellationToken);
        var entry = await dbSet.AddAsync(entity, cancellationToken);
        await SaveChangesIfNeededAsync(cancellationToken);
        return entry.Entity;
    }

    /// <inheritdoc />
    public virtual async Task InsertManyAsync([NotNull] IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
    {
        var dbSet = await GetDbSetAsync(cancellationToken);
        await dbSet.AddRangeAsync(entities, cancellationToken);
        await SaveChangesIfNeededAsync(cancellationToken);
    }

    /// <inheritdoc />
    public virtual async Task<TEntity> UpdateAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        var dbContext = await GetDbContextAsync(cancellationToken);
        var entry = dbContext.Entry(entity);
        if (entry.State == EntityState.Detached)
        {
            var dbSet = await GetDbSetAsync(cancellationToken);
            entry = dbSet.Update(entity);
        }

        await SaveChangesIfNeededAsync(cancellationToken);
        return entry.Entity;
    }

    /// <inheritdoc />
    public virtual async Task UpdateManyAsync([NotNull] IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
    {
        var dbSet = await GetDbSetAsync(cancellationToken);
        dbSet.UpdateRange(entities);
        await SaveChangesIfNeededAsync(cancellationToken);
    }

    /// <inheritdoc />
    public virtual async Task DeleteAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        var dbSet = await GetDbSetAsync(cancellationToken);
        dbSet.Remove(entity);
        await SaveChangesIfNeededAsync(cancellationToken);
    }

    /// <inheritdoc />
    public virtual async Task DeleteManyAsync([NotNull] IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
    {
        var dbSet = await GetDbSetAsync(cancellationToken);
        dbSet.RemoveRange(entities);
        await SaveChangesIfNeededAsync(cancellationToken);
    }

    /// <inheritdoc />
    public virtual async Task DeleteManyAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
    {
        var dbSet = await GetDbSetAsync(cancellationToken);
        var entities = await dbSet.Where(predicate).ToListAsync(cancellationToken);
        dbSet.RemoveRange(entities);
        await SaveChangesIfNeededAsync(cancellationToken);
    }

    /// <summary>
    /// 在工作单元外立即保存更改。
    /// </summary>
    protected async Task SaveChangesIfNeededAsync(CancellationToken cancellationToken = default)
    {
        if (Uow.Current != null)
        {
            return;
        }

        var dbContext = await GetDbContextAsync(cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}


/// <summary>
/// 使用 EF Core 访问具有强类型主键的实体。
/// </summary>
/// <typeparam name="TDbContext">上下文类型。</typeparam>
/// <typeparam name="TEntity">实体类型。</typeparam>
/// <typeparam name="TKey">主键类型。</typeparam>
public class EfCoreRepository<TDbContext, TEntity, TKey>(
    IDbContextProvider<TDbContext> dbContextProvider,
    IUnitOfWorkManager uow) : EfCoreRepository<TDbContext, TEntity>(dbContextProvider, uow), IRepository<TEntity, TKey>
    where TDbContext : DbContext
    where TKey : IEquatable<TKey>
    where TEntity : class, IEntity<TKey>
{

    /// <inheritdoc />
    public virtual async Task<TEntity?> GetByIdAsync(TKey id, CancellationToken cancellationToken = default)
    {
        var dbSet = await GetDbSetAsync(cancellationToken);
        // 必须走 LINQ 查询而非 FindAsync：FindAsync 绕过全局查询过滤器（软删除/租户隔离），
        // 会让按 Id 的读取越过隔离边界
        return await dbSet.FirstOrDefaultAsync(e => e.Id.Equals(id), cancellationToken);
    }

    /// <inheritdoc />
    public virtual async Task DeleteAsync(TKey id, CancellationToken cancellationToken = default)
    {
        var entity = await GetByIdAsync(id, cancellationToken);
        if (entity != null)
        {
            await DeleteAsync(entity, cancellationToken);
        }
    }

    /// <inheritdoc />
    public virtual async Task DeleteManyAsync([NotNull] IEnumerable<TKey> ids, CancellationToken cancellationToken = default)
    {
        var dbSet = await GetDbSetAsync(cancellationToken);
        var entities = await dbSet.Where(e => ids.Contains(e.Id)).ToListAsync(cancellationToken);
        dbSet.RemoveRange(entities);
        await SaveChangesIfNeededAsync(cancellationToken);
    }
}
