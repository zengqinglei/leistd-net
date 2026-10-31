#if (LocalIdentity && IncludeMultiTenancy)
using CompanyName.ProjectName.Domain.Users.Entities;
using CompanyName.ProjectName.Infrastructure.Persistence;
using Leistd.MultiTenancy.EntityFrameworkCore.Entities;
using Leistd.UnitOfWork;
using Leistd.UnitOfWork.EntityFrameworkCore.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CompanyName.ProjectName.IntegrationTests;

/// <summary>宿主库同时承载业务与控制数据时，两个获取顺序都必须共用事务。</summary>
public sealed class ControlContextSharedTransactionTests(ProjectWebApplicationFactory factory)
    : IClassFixture<ProjectWebApplicationFactory>
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Business_and_control_writes_commit_or_roll_back_together(bool controlFirst, bool complete)
    {
        var name = $"shared-{Guid.NewGuid():N}";
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var services = scope.ServiceProvider;
            using var uow = services.GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true);
            var businessProvider = services.GetRequiredService<IDbContextProvider<MyProjectDbContext>>();
            var controlProvider = services.GetRequiredService<IDbContextProvider<IdentityControlDbContext>>();
            MyProjectDbContext business;
            IdentityControlDbContext control;
            if (controlFirst)
            {
                control = await controlProvider.GetDbContextAsync();
                business = await businessProvider.GetDbContextAsync();
            }
            else
            {
                business = await businessProvider.GetDbContextAsync();
                control = await controlProvider.GetDbContextAsync();
            }
            Assert.Same(business.Database.GetDbConnection(), control.Database.GetDbConnection());
            business.Add(new Role(name, name));
            control.Add(new TenantRecord { Name = name, NormalizedName = name.ToUpperInvariant() });
            await uow.SaveChangesAsync();
            if (complete)
                await uow.CompleteAsync();
            else
                await uow.RollbackAsync();
        }
        await using var verification = factory.Services.CreateAsyncScope();
        Assert.Equal(complete, await verification.ServiceProvider.GetRequiredService<MyProjectDbContext>()
            .Set<Role>().AnyAsync(x => x.Name == name));
        Assert.Equal(complete, await verification.ServiceProvider.GetRequiredService<IdentityControlDbContext>()
            .Set<TenantRecord>().AnyAsync(x => x.Name == name));
    }
}
#endif
