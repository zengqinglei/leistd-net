namespace Leistd.Auditing.Abstractions;

/// <summary>
/// 标记具有创建时间的对象。
/// </summary>
public interface IHasCreationTime
{
    /// <summary>
    /// 获取创建时间。
    /// </summary>
    DateTime CreationTime { get; }
}

/// <summary>
/// 标记具有创建者信息的对象。
/// </summary>
public interface ICreationAuditedObject : IHasCreationTime
{
    /// <summary>
    /// 获取创建者标识。
    /// </summary>
    string? CreatorId { get; }
}

/// <summary>
/// 标记具有最后修改时间的对象。
/// </summary>
public interface IHasModificationTime
{
    /// <summary>
    /// 获取最后修改时间。
    /// </summary>
    DateTime? LastModificationTime { get; }
}

/// <summary>
/// 标记具有最后修改者信息的对象。
/// </summary>
public interface IModificationAuditedObject : IHasModificationTime
{
    /// <summary>
    /// 获取最后修改者标识。
    /// </summary>
    string? LastModifierId { get; }
}

/// <summary>
/// 标记具有删除时间的对象。
/// </summary>
public interface IHasDeletionTime
{
    /// <summary>
    /// 获取删除时间。
    /// </summary>
    DateTime? DeletionTime { get; }
}

/// <summary>
/// 标记支持软删除的对象。
/// </summary>
public interface ISoftDelete
{
    /// <summary>
    /// 获取对象是否已删除。
    /// </summary>
    bool IsDeleted { get; }
}

/// <summary>
/// 标记具有删除审计信息的对象。
/// </summary>
public interface IDeletionAuditedObject : IHasDeletionTime, ISoftDelete
{
    /// <summary>
    /// 获取删除者标识。
    /// </summary>
    string? DeleterId { get; }
}

/// <summary>
/// 组合创建、修改和删除审计契约。
/// </summary>
public interface IFullAuditedObject :
    ICreationAuditedObject,
    IModificationAuditedObject,
    IDeletionAuditedObject
{
}
