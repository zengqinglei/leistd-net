namespace Leistd.Ddd.Domain.Entities;

/// <summary>
/// 标记作为一致性边界入口的聚合根。
/// </summary>
/// <remarks>
/// <b>本标记不被框架强制</b>：<c>IRepository&lt;TEntity&gt;</c> 的约束是 <c>IEntity</c>，不是本接口。
/// 它的价值是可读与可静态检查（审查、架构测试与 AI 判断边界），领域事件仍在 <see cref="Entity"/> 上。
/// </remarks>
public interface IAggregateRoot : IEntity
{
}

/// <summary>
/// 标记具有强类型主键的聚合根。
/// </summary>
/// <typeparam name="TKey">主键类型</typeparam>
public interface IAggregateRoot<TKey> : IAggregateRoot, IEntity<TKey>
{
}
