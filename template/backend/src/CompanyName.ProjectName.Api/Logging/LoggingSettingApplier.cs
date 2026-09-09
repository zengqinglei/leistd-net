using CompanyName.ProjectName.Application.Settings.Hosting;
using CompanyName.ProjectName.Application.Settings.Provider;
using Leistd.Settings.Abstractions;
using Leistd.Settings.Definitions;
using Serilog.Events;

namespace CompanyName.ProjectName.Api.Logging;

/// <summary>
/// 把日志设置推到进程内的开关上
/// </summary>
/// <remarks>
/// 认不出的级别只记一条告警并保留当前值，不抛异常：这条路径既在启动期跑、也在写入后跑，
/// 为一个打错的级别把启动或保存打断，代价远大于继续用上一个级别。
/// 写入端本来就校验过取值（见 <c>SettingAppService</c>），能走到这里的非法值只可能来自
/// 直接改库或跨版本遗留数据。
/// </remarks>
/// <param name="state">进程内的日志状态。</param>
/// <param name="settingStore">
/// 直接读宿主层那一行。
/// <para>
/// 刻意<b>不用</b> <c>ISettingProvider</c>：它按请求记忆化，而这个应用器会在"写完设置之后"
/// 的同一个请求里被调用——只要该请求早前有任何一处读过设置（本地化、时区之类），
/// 记忆化的就是<b>写入前</b>的快照，于是"改完立即生效"应用的是旧值。
/// 直接读存储没有这层缓存。
/// </para>
/// </param>
/// <param name="definitionManager">取代码默认值：宿主层没有行时回落到它。</param>
/// <param name="logger">用于报告认不出的取值。</param>
public sealed class LoggingSettingApplier(
    LoggingSettingState state,
    ISettingStore settingStore,
    ISettingDefinitionManager definitionManager,
    ILogger<LoggingSettingApplier> logger) : IHostSettingApplier
{
    /// <inheritdoc />
    public async Task ApplyAsync(CancellationToken cancellationToken = default)
    {
        var stored = await settingStore.GetAllAsync(SettingScopes.Host, userId: null, cancellationToken);

        state.MinimumLevel.MinimumLevel = Resolve(
            SettingConstant.Logging.MinimumLevel, stored, state.MinimumLevel.MinimumLevel);

        state.RequestLevel = Resolve(
            SettingConstant.Logging.RequestLevel, stored, state.RequestLevel);
    }

    private LogEventLevel Resolve(
        string name,
        IReadOnlyDictionary<string, string> stored,
        LogEventLevel current)
    {
        // 宿主层没有覆盖值就回落到代码默认值：清除设置要能把级别退回默认，
        // 只看有没有行的话，清除之后仍会停在上一次设过的级别。
        var raw = stored.GetValueOrDefault(name) ?? definitionManager.GetOrNull(name)?.DefaultValue;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return current;
        }

        if (Enum.TryParse<LogEventLevel>(raw, ignoreCase: false, out var level))
        {
            return level;
        }

        logger.LogWarning(
            "Setting {SettingName} holds '{Value}', which is not a Serilog level; keeping {CurrentLevel}.",
            name, raw, current);
        return current;
    }
}
