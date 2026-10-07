using System.Reflection;
using CompanyName.ProjectName.Domain.Users.Entities;
using CompanyName.ProjectName.Infrastructure.Persistence;
using Leistd.MultiTenancy.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;

namespace CompanyName.ProjectName.IntegrationTests;

/// <summary>
/// 实体模型约定：本项目实体实现 <see cref="IMultiTenant"/>（auth.md），枚举属性按字符串持久化（coding-backend.md §3.8）。
/// </summary>
/// <remarks>
/// 判定在业务上下文的实际模型上做，只检查本项目 Domain 程序集里的实体；组件自带的实体由各组件负责。
/// 漏实现 <see cref="IMultiTenant"/> 的实体不受租户过滤、也不落租户归属，数据会在租户间可见；
/// 枚举按整数存时，调整成员顺序就会把历史数据静默改成另一个值。
/// </remarks>
public sealed class EntityModelConventionTests(ProjectWebApplicationFactory factory)
    : IClassFixture<ProjectWebApplicationFactory>
{
    /// <summary>
    /// 不实现 <see cref="IMultiTenant"/> 的本项目实体，每项写明理由。当前没有。
    /// </summary>
    private static readonly EntityExemption[] Exemptions = [];

    /// <summary>
    /// 业务上下文里的本项目实体全部满足约定
    /// </summary>
    [Fact]
    public void Project_entities_are_tenant_scoped_and_store_enums_as_strings()
    {
        using var scope = factory.Services.CreateScope();
        var model = scope.ServiceProvider.GetRequiredService<MyProjectDbContext>().Model;
        var domain = typeof(User).Assembly;

        // 防止程序集过滤失效后变成"零个实体、零个问题"的空转通过
        Assert.Contains(model.GetEntityTypes(), entity => entity.ClrType == typeof(User));
        Assert.Empty(FindViolations(model, domain, Exemptions));
    }

    /// <summary>
    /// 合规实体通过；缺租户归属、枚举按整数存的各报一条；白名单内的实体只免租户归属
    /// </summary>
    [Fact]
    public void Synthetic_model_reports_missing_tenant_scope_and_integer_enums()
    {
        var model = SyntheticModel();
        var fixtures = typeof(EntityModelConventionTests).Assembly;

        var violations = FindViolations(model, fixtures, []);

        Assert.Equal(2, violations.Count);
        Assert.Contains(violations, v => v.Contains(nameof(HostOnlyOrder)) && v.Contains(nameof(IMultiTenant)));
        Assert.Contains(violations, v => v.Contains($"{nameof(HostOnlyOrder)}.{nameof(HostOnlyOrder.Status)}") && v.Contains("string"));

        var exempted = FindViolations(model, fixtures, [new(typeof(HostOnlyOrder), "夹具：宿主数据")]);
        Assert.Contains($"{nameof(HostOnlyOrder)}.{nameof(HostOnlyOrder.Status)}", Assert.Single(exempted));
    }

    private static List<string> FindViolations(IModel model, Assembly projectAssembly, IReadOnlyList<EntityExemption> exemptions)
    {
        var violations = new List<string>();
        foreach (var entity in model.GetEntityTypes().Where(e => e.ClrType.Assembly == projectAssembly && !e.IsOwned()))
        {
            if (!typeof(IMultiTenant).IsAssignableFrom(entity.ClrType) && exemptions.All(e => e.EntityType != entity.ClrType))
            {
                violations.Add($"{entity.ClrType.Name}: entity must implement {nameof(IMultiTenant)} or be exempted");
            }

            foreach (var property in entity.GetProperties())
            {
                var type = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
                var provider = property.GetProviderClrType() ?? property.GetValueConverter()?.ProviderClrType;
                if (type.IsEnum && provider != typeof(string))
                {
                    violations.Add($"{entity.ClrType.Name}.{property.Name}: enum must be stored as string");
                }
            }
        }

        return violations;
    }

    private static IModel SyntheticModel()
    {
        var builder = new ModelBuilder();
        builder.Entity<TenantScopedOrder>(entity =>
        {
            entity.HasKey(order => order.Id);
            entity.Property(order => order.TenantId);
            entity.Property(order => order.Status).HasConversion<string>();
        });
        builder.Entity<HostOnlyOrder>(entity =>
        {
            entity.HasKey(order => order.Id);
            entity.Property(order => order.Status);
        });
        return builder.FinalizeModel();
    }

    private sealed record EntityExemption(Type EntityType, string Reason);

    private enum OrderStatus
    {
        Draft,
        Placed,
    }

    private sealed class TenantScopedOrder : IMultiTenant
    {
        public Guid Id { get; set; }

        public Guid? TenantId { get; set; }

        public OrderStatus? Status { get; set; }
    }

    private sealed class HostOnlyOrder
    {
        public Guid Id { get; set; }

        public OrderStatus Status { get; set; }
    }
}
