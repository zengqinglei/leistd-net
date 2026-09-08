namespace Leistd.Ddd.Infrastructure.HostedServices;

// 宿主注册过的 Microsoft.EntityFrameworkCore.DbContext 类型集合
// 由 AddDddDbContext<TDbContext>() 显式登记，供 MultiTenantFilterGuard 在启动期逐个核对模型。
// 刻意不扫描容器：扫描得到的覆盖面取决于宿主怎么注册 DbContext（工厂委托注册看不到实现类型，
// 不装 provider factory 则回调根本不执行），于是一道安全闸门的有效性挂在无关的注册形态上。
// 单独一个类型而不是直接注册 HashSet<Type>：容器里放裸集合类型容易与别处撞。
internal sealed class TrackedDbContextTypes
{
    private readonly HashSet<Type> _types = [];

    public IReadOnlyCollection<Type> Types => _types;

    public void Add(Type dbContextType) => _types.Add(dbContextType);
}
