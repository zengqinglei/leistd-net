using CompanyName.ProjectName.Domain.Users.Entities;
using CompanyName.ProjectName.Domain.Users.Repositories;
using Leistd.Ddd.Infrastructure.Persistence.Repositories;
using Leistd.UnitOfWork;
using Leistd.UnitOfWork.EntityFrameworkCore.Database;
using Microsoft.EntityFrameworkCore;

namespace CompanyName.ProjectName.Infrastructure.Persistence.Repositories;

/// <summary>
/// <see cref="IUserRepository"/> 的 EF Core 实现。
/// </summary>
public class EfCoreUserRepository(
    IDbContextProvider<MyProjectDbContext> dbContextProvider,
    IUnitOfWorkManager uow)
    : EfCoreRepository<MyProjectDbContext, User, Guid>(dbContextProvider, uow), IUserRepository
{
    /// <inheritdoc />
    /// <remarks>
    /// 关联行与角色一次连接查询取回；两边的 DbSet 各自带租户与软删除的全局过滤器。
    /// </remarks>
    public async Task<List<string>> GetRoleNamesAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var dbContext = await GetDbContextAsync(cancellationToken);
        var query =
            from userRole in dbContext.UserRoles
            join role in dbContext.Roles on userRole.RoleId equals role.Id
            where userRole.UserId == userId
            orderby role.Name
            select role.Name;

        return await query.ToListAsync(cancellationToken);
    }
}
