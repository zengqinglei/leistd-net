using System.Linq.Expressions;

namespace Leistd.Authorization.DataScope;

/// <summary>
/// 内置数据操作名称。业务可以自行扩展，本类只是常用值的约定。
/// </summary>
/// <remarks>
/// 读、改、删、导出可以使用不同的范围策略：不能假设"能看就能改"。
/// 但列表、总数、导出和批量操作必须使用同一个范围入口，否则分页总数与实际可见数据会不一致。
/// </remarks>
public static class DataOperations
{
    /// <summary>读取（列表、详情、统计）。</summary>
    public const string Read = "Read";

    /// <summary>修改。</summary>
    public const string Update = "Update";

    /// <summary>删除。</summary>
    public const string Delete = "Delete";

    /// <summary>导出。</summary>
    public const string Export = "Export";
}

/// <summary>
/// 数据范围定义：某类资源上一种可被分配的范围。
/// </summary>
/// <remarks>
/// Framework 不内置"本人""本部门""下级部门"等业务概念，只定义可注册的元数据结构；
/// 具体语义由业务实现的 <see cref="IDataScopeProvider{TEntity}"/> 决定。
/// </remarks>
/// <param name="ResourceName">资源类型名称，如 <c>Orders</c>。</param>
/// <param name="Operation">该范围适用的操作，见 <see cref="DataOperations"/>。</param>
/// <param name="ScopeName">范围名称，如 <c>Own</c>、<c>Organization</c>。</param>
/// <param name="DisplayName">显示名称，供管理界面渲染。</param>
public sealed record DataScopeDefinition(
    string ResourceName,
    string Operation,
    string ScopeName,
    string? DisplayName = null);

/// <summary>
/// 数据范围分配：某个主体在某类资源的某个操作上被授予的一种范围。
/// </summary>
/// <remarks>
/// 范围按操作分别分配，因为"能看"不等于"能改"：
/// <code>
/// Orders / Read   : All | Own | Organization
/// Orders / Update : Own | Organization
/// </code>
/// </remarks>
/// <param name="ResourceName">资源类型名称。</param>
/// <param name="Operation">该分配适用的操作。</param>
/// <param name="ScopeName">范围名称。</param>
/// <param name="ScopeValue">
/// 范围参数，语义由 Provider 解释，例如组织 ID。通用存储只保存这个字符串，
/// 组织树展开、项目成员等业务关系仍由业务表负责，不复制到通用授权表。
/// </param>
public sealed record DataScopeAssignment(
    string ResourceName,
    string Operation,
    string ScopeName,
    string? ScopeValue = null);

/// <summary>
/// 数据范围解析上下文。
/// </summary>
/// <param name="Subject">当前主体。</param>
/// <param name="ResourceName">资源类型名称。</param>
/// <param name="Operation">本次查询的操作，见 <see cref="DataOperations"/>。</param>
/// <param name="Assignments">当前主体在该资源该操作上被分配的全部范围。</param>
public sealed record DataScopeContext(
    PermissionSubject Subject,
    string ResourceName,
    string Operation,
    IReadOnlyList<DataScopeAssignment> Assignments);

/// <summary>
/// 数据范围提供器：把一种范围翻译成可由数据库执行的查询谓词。
/// </summary>
/// <typeparam name="TEntity">被过滤的实体类型。</typeparam>
/// <remarks>
/// 返回谓词而不是直接改写查询，是为了让多个被分配的范围可以做并集：
/// 一个主体常常同时拥有多种范围（例如"本人"加"某几个组织"），
/// 只有拿到各自的谓词才能 OR 起来，改写查询无法表达这种组合。
/// <para>
/// 实现必须返回可被数据库 Provider 翻译的表达式，不得在其中调用只能客户端求值的方法，
/// 也不得先把候选数据加载到内存再过滤——否则分页总数、排序、导出和性能都会错误。
/// </para>
/// </remarks>
public interface IDataScopeProvider<TEntity>
{
    /// <summary>本 Provider 负责的资源类型名称。</summary>
    string ResourceName { get; }

    /// <summary>本 Provider 负责的范围名称。</summary>
    string ScopeName { get; }

    /// <summary>
    /// 构造该范围对应的查询谓词。
    /// </summary>
    /// <returns>
    /// 谓词；返回 <c>null</c> 表示该范围不施加任何限制（等价于"全部可见"）。
    /// </returns>
    ValueTask<Expression<Func<TEntity, bool>>?> BuildPredicateAsync(
        DataScopeContext context,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 数据范围分配的来源，由业务项目实现。
/// </summary>
/// <remarks>
/// Framework 不规定分配存放在哪里：可以来自角色配置表、组织架构或外部策略服务。
/// </remarks>
public interface IDataScopeAssignmentProvider
{
    /// <summary>
    /// 获取指定主体在某类资源的某个操作上的全部范围分配。
    /// </summary>
    /// <remarks>
    /// 返回空集合表示该主体在此操作上没有任何可见范围，应用器将据此返回空结果集（默认拒绝）。
    /// </remarks>
    ValueTask<IReadOnlyList<DataScopeAssignment>> GetAssignmentsAsync(
        PermissionSubject subject,
        string resourceName,
        string operation,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 数据范围应用入口：把当前主体的可见范围合并进业务查询。
/// </summary>
/// <remarks>
/// 列表、总数、导出和批量操作必须全部经由本接口取得候选集合，才能保证四者一致。
/// </remarks>
public interface IDataScopeApplier
{
    /// <summary>
    /// 对查询施加当前主体在该资源上的可见范围。
    /// </summary>
    /// <returns>
    /// 施加范围后的查询。主体不可识别时返回空结果集（默认拒绝）；
    /// 主体是超级管理员或被分配的范围中存在"无限制"时，原样返回。
    /// </returns>
    ValueTask<IQueryable<TEntity>> ApplyAsync<TEntity>(
        IQueryable<TEntity> query,
        string resourceName,
        string operation,
        CancellationToken cancellationToken = default);
}
