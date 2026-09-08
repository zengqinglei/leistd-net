using Leistd.Ddd.Domain.Entities;
using Leistd.Ddd.Domain.Repositories;

namespace Leistd.Ddd.Infrastructure;

/// <summary>
/// 单个 DbContext 接入 DDD 基础设施时的仓储注册选项。
/// </summary>
/// <remarks>
/// <para>仓储注册是<b>显式开关</b>：不调用 <see cref="AddDefaultRepositories"/> 就不注册任何仓储。
/// 只需要工作单元与租户过滤器闸门的上下文（典型是控制面上下文）保持默认即可。</para>
/// <para>冲突顺序是确定的：自定义仓储先落定，默认注册跳过已有自定义的实体。
/// 不存在"后注册静默胜出"这种由调用顺序决定的结果。</para>
/// </remarks>
public sealed class DddDbContextOptions
{
    internal bool RegisterDefaultRepositories { get; private set; }

    internal Dictionary<Type, Type> CustomRepositories { get; } = [];

    internal List<Type> ExplicitEntities { get; } = [];

    /// <summary>
    /// 为本上下文<b>按 <c>DbSet&lt;T&gt;</c> 声明</b>派生的实体注册默认仓储。
    /// </summary>
    /// <remarks>
    /// 实体来源是公开 <c>DbSet&lt;T&gt;</c> 属性中实现 <see cref="IEntity"/> 的类型。
    /// 仅经模型配置映射的实体需用 <see cref="AddDefaultRepository{TEntity}"/> 显式登记。
    /// </remarks>
    public DddDbContextOptions AddDefaultRepositories()
    {
        RegisterDefaultRepositories = true;
        return this;
    }

    /// <summary>
    /// 为 <typeparamref name="TEntity"/> 注册自定义仓储实现，优先于默认注册。
    /// </summary>
    public DddDbContextOptions AddRepository<TEntity, TImplementation>()
        where TEntity : class, IEntity
        where TImplementation : class, IRepository<TEntity>
    {
        CustomRepositories[typeof(TEntity)] = typeof(TImplementation);
        return this;
    }

    /// <summary>
    /// 点名为 <typeparamref name="TEntity"/> 注册默认仓储，用于未暴露 <c>DbSet&lt;T&gt;</c> 的实体。
    /// </summary>
    public DddDbContextOptions AddDefaultRepository<TEntity>()
        where TEntity : class, IEntity
    {
        ExplicitEntities.Add(typeof(TEntity));
        return this;
    }
}
