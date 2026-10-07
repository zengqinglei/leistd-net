namespace Leistd.Ddd.Domain.Entities;

/// <summary>标记作为一致性边界入口的聚合根。</summary>
/// <remarks>
/// 框架不强制本标记（<c>IRepository&lt;TEntity&gt;</c> 约束的是 <c>IEntity</c>），用于审查与架构测试判断聚合边界。
/// </remarks>
public interface IAggregateRoot : IEntity
{
}

/// <summary>标记具有强类型主键的聚合根。</summary>
/// <typeparam name="TKey">主键类型。</typeparam>
public interface IAggregateRoot<TKey> : IAggregateRoot, IEntity<TKey>
{
}
