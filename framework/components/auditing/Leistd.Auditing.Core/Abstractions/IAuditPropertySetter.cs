namespace Leistd.Auditing.Abstractions;

/// <summary>
/// 通过持久化适配层填充实体审计属性。
/// </summary>
/// <remarks>
/// 负责自动设置实体的审计属性（创建时间、修改时间、删除时间等）。
/// 参数声明为 <c>object</c> 是为了让本组件不依赖 EF Core，<b>而不是允许传实体本身</b>：
/// EF Core 适配层要求传入 <c>EntityEntry</c>，传别的类型会抛 <see cref="ArgumentException"/>。
/// </remarks>
public interface IAuditPropertySetter
{
    /// <summary>
    /// 填充未设置的创建审计属性。
    /// </summary>
    /// <param name="entityEntry">实体变更追踪条目。EF Core 适配层要求 <c>EntityEntry</c></param>
    void SetCreationProperties(object entityEntry);

    /// <summary>
    /// 更新本次修改的审计属性。
    /// </summary>
    /// <param name="entityEntry">实体变更追踪条目。EF Core 适配层要求 <c>EntityEntry</c></param>
    void SetModificationProperties(object entityEntry);

    /// <summary>
    /// 填充未设置的软删除审计属性。
    /// </summary>
    /// <param name="entityEntry">实体变更追踪条目。EF Core 适配层要求 <c>EntityEntry</c></param>
    void SetDeletionProperties(object entityEntry);
}
