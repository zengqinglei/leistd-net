using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Leistd.Authorization.DataScope;

/// <summary>
/// 数据范围依赖注入扩展。
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// 注册数据范围应用器。
    /// </summary>
    /// <remarks>
    /// 依赖调用方已注册 <see cref="IPermissionSubjectProvider"/> 与
    /// <see cref="IDataScopeAssignmentProvider"/>；后者由业务项目提供，
    /// 决定"当前主体在某类资源上被分配了哪些范围"。
    /// </remarks>
    public static IServiceCollection AddDataScopeCore(this IServiceCollection services)
    {
        services.TryAddScoped<IDataScopeApplier, DefaultDataScopeApplier>();
        return services;
    }

    /// <summary>
    /// 注册某个实体的一种数据范围 Provider。同一实体可以注册多个不同范围。
    /// </summary>
    public static IServiceCollection AddDataScopeProvider<TEntity, TProvider>(
        this IServiceCollection services)
        where TProvider : class, IDataScopeProvider<TEntity>
    {
        services.AddScoped<IDataScopeProvider<TEntity>, TProvider>();
        return services;
    }
}
