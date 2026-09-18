using Leistd.OperationRecords.Abstractions;
using Leistd.OperationRecords.Options;
using Leistd.OperationRecords.Services;
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
    /// 还需要一个 <see cref="IOperationRecordStore"/> 实现（如
    /// <c>AddOperationRecordsEfCore&lt;TDbContext&gt;()</c>）：它是记录器的必需依赖，
    /// 缺失时解析 <see cref="IOperationRecorder"/> 直接失败，而不是静默什么都不记。
    /// </remarks>
    /// <example>
    /// <code>
    /// builder.Services.AddOperationRecordsEfCore&lt;AppDbContext&gt;();   // 内部已调用 AddOperationRecords()
    ///
    /// // 用例里显式记录
    /// await operationRecorder.RecordSucceededAsync(
    ///     OperationRecordActions.UserCreated, user.Id.ToString(), "App.Users.Create", ct);
    /// </code>
    /// </example>
    /// <param name="services">服务集合。</param>
    public static IServiceCollection AddOperationRecords(this IServiceCollection services)
    {
        // 显式建立选项，宿主不调配置重载时 IOptions<OperationRecordOptions> 也解析得出默认值。
        services.AddOptions<OperationRecordOptions>();

        // 幂等：EF 包的注册入口会调到这里，宿主自己也可能显式调一次。
        // 不幂等会让 IOperationRecorder 出现两条，按 IEnumerable 解析时重复记录。
        services.TryAddTransient<IOperationRecorder, OperationRecorder>();

        // 动作定义索引是启动期事实，单例即可；宿主用
        // AddSingleton<IOperationActionDefinitionProvider, XxxProvider>() 登记自己的动作。
        // 宿主一个都不登记时这里得到空索引——界面按"未登记码"降级为原样显示裸码，
        // 而不是让整页读不出来：审计记录是既成事实，不能因为定义缺失就取不到。
        services.TryAddSingleton<IOperationActionDefinitionManager, OperationActionDefinitionManager>();
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
