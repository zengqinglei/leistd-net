using Leistd.OperationRecords.Definitions;
using Leistd.OperationRecords.Queries;
using Leistd.OperationRecords.Recording;
using Leistd.OperationRecords.Stores;
using Leistd.OperationRecords.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Leistd.OperationRecords;

/// <summary>
/// 提供操作记录核心服务注册入口。
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// 注册操作记录的记录器，识别真实操作人的 claim 类型用默认值。
    /// </summary>
    /// <remarks>
    /// <para>还需要一个 <see cref="IOperationRecordWriter"/> 实现（数据库存储
    /// <c>AddOperationRecordsEfCore&lt;TDbContext&gt;()</c>，或结构化日志输出 <c>AddOperationRecordsLogging()</c>）：
    /// 它是记录器的必需依赖，缺失时解析 <see cref="IOperationRecorder"/> 直接失败，而不是静默什么都不记。</para>
    /// <para>不注册历史查询：查询只在有可回读存储时成立，由存储适配调用 <see cref="AddOperationRecordQueries"/>。</para>
    /// </remarks>
    /// <example>
    /// <code>
    /// builder.Services.AddOperationRecordsEfCore&lt;AppDbContext&gt;();   // 内部已调用 AddOperationRecords()
    ///
    /// // 用例里显式记录
    /// await operationRecorder.RecordSucceededAsync(
    ///     OperationRecordActions.UserCreated,
    ///     OperationTarget.For(user.Id, user.DisplayName ?? user.Username),
    ///     "App.Users.Create",
    ///     ct);
    /// </code>
    /// </example>
    /// <param name="services">服务集合。</param>
    public static IServiceCollection AddOperationRecords(this IServiceCollection services)
    {
        // 显式建立选项，宿主不传配置委托时 IOptions<OperationRecordOptions> 也解析得出默认值。
        services.AddOptions<OperationRecordOptions>();

        // 幂等：EF 包的注册入口会调到这里，宿主自己也可能显式调一次。
        // 不幂等会让 IOperationRecorder 出现两条，按 IEnumerable 解析时重复记录。
        // 失败记录去重的作用域状态：应用服务在拒绝处记下的那条胜出，端点兜底遇到同一动作码就跳过。
        // 必须是 Scoped——记录器是 Transient，状态放在它身上会随每次解析重置，去重就失效了。
        services.TryAddScoped<RecordedFailureTracker>();
        services.TryAddTransient<IOperationRecorder, OperationRecorder>();

        // 动作定义索引是启动期事实，单例即可；宿主用
        // AddSingleton<IOperationActionDefinitionProvider, XxxProvider>() 登记自己的动作。
        // 写入要求动作码已登记；读取时遇到已不再登记的历史码，界面降级为原样显示裸码，
        // 而不是让整页读不出来：审计记录是既成事实，不能因为定义缺失就取不到。
        services.TryAddSingleton<IOperationActionDefinitionManager, OperationActionDefinitionManager>();
        return services;
    }

    /// <summary>
    /// 注册历史查询与导出用例（<see cref="IOperationRecordQueryService"/>）。
    /// </summary>
    /// <remarks>
    /// 由能回读历史的存储适配调用（如 <c>AddOperationRecordsEfCore&lt;TDbContext&gt;()</c>），宿主通常不直接调用。
    /// 它要求一个 <see cref="IOperationRecordReader"/>：只写不读的输出适配不提供读取，也就没有查询可注册。
    /// </remarks>
    /// <param name="services">服务集合。</param>
    public static IServiceCollection AddOperationRecordQueries(this IServiceCollection services)
    {
        services.AddOperationRecords();
        services.TryAddTransient<IOperationRecordQueryService, OperationRecordQueryService>();
        return services;
    }

    /// <summary>
    /// 注册操作记录的记录器，并以委托配置选项。
    /// </summary>
    /// <remarks>
    /// 宿主签发的 claim 用了别的名字时从这里改——组件不把 claim 名写死，
    /// 因为"主体里那个字段叫什么"是宿主的技术细节。
    /// </remarks>
    /// <example>
    /// <code>
    /// builder.Services.AddOperationRecords(options =>
    /// {
    ///     options.ImpersonatorIdClaimType = "act_sub";
    /// });
    /// </code>
    /// </example>
    /// <param name="services">服务集合。</param>
    /// <param name="configureOptions">选项配置委托。</param>
    public static IServiceCollection AddOperationRecords(
        this IServiceCollection services,
        Action<OperationRecordOptions> configureOptions)
    {
        services.Configure(configureOptions);
        return services.AddOperationRecords();
    }
}
