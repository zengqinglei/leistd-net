namespace Leistd.Settings.Dtos;

/// <summary>一项设置在各层级上的覆盖值、可写层级与值元数据。</summary>
/// <remarks>
/// 给的是各层级的原始覆盖值而不是回落后的生效值，供设置页分层编辑；<see langword="null"/> 表示该层未覆盖。
/// 层级用布尔量表达，不受宿主枚举序列化方式影响。
/// </remarks>
/// <param name="Name">设置名称。</param>
/// <param name="DisplayName">已按请求 culture 翻译的显示名称。</param>
/// <param name="Group">所属分组的稳定标识；未分组的归入默认分组。</param>
/// <param name="GroupDisplayName">已按请求 culture 翻译的分组名称。</param>
/// <param name="UserValue">当前用户的个人覆盖值；<see langword="null"/> 表示未覆盖。</param>
/// <param name="TenantValue">当前租户（宿主上下文下为宿主）的覆盖值；进程级设置的值也在这里。</param>
/// <param name="DefaultValue">代码默认值。</param>
/// <param name="AllowsTenantScope">是否允许写入租户级默认值。</param>
/// <param name="AllowsUserScope">是否允许用户覆盖为个人偏好。</param>
/// <param name="AllowsHostScope">是否是进程级设置；为 <see langword="true"/> 时另两个层级标记都是 <see langword="false"/>。</param>
/// <param name="Minimum">整数设置的下界；其它为 <see langword="null"/>。</param>
/// <param name="Maximum">整数设置的上界；其它为 <see langword="null"/>。</param>
/// <param name="IsBoolean">布尔设置：值只能是 <c>true</c> / <c>false</c>。</param>
/// <param name="IsSecret">机密设置：各层的值与默认值一律不下发，界面渲染成只写的输入框。</param>
/// <param name="HasSecretValue">机密设置在可写的那一层是否已经设过值；非机密设置恒为 <see langword="false"/>。</param>
/// <param name="AllowedValues">候选值；不限时为 <see langword="null"/>。</param>
/// <param name="CanManage">调用方能否写入租户层（宿主上下文下为进程级）的值，由 HTTP 端点按授权策略计算；
/// 直接调用 <see cref="Management.ISettingManagementService"/> 时恒为 <see langword="false"/>。</param>
/// <param name="UserModification"><see cref="UserValue"/> 那一层最后一次写入的时间与写入人；该层没有值时为 <see langword="null"/>。</param>
/// <param name="TenantModification"><see cref="TenantValue"/> 那一层最后一次写入的时间与写入人；机密设置同样给出，不含值。</param>
/// <param name="IsCheckable">这一项登记了测试（<c>AddSettingCheck</c>），设置页在这一行提供"测试"。</param>
/// <param name="LastCheck">本租户（宿主上下文为宿主）最近一次测试的结果；没测过或没有登记测试时为 <see langword="null"/>。</param>
public record SettingOutputDto(
    string Name,
    string DisplayName,
    string Group,
    string GroupDisplayName,
    string? UserValue,
    string? TenantValue,
    string? DefaultValue,
    bool AllowsTenantScope,
    bool AllowsUserScope,
    bool AllowsHostScope,
    int? Minimum = null,
    int? Maximum = null,
    bool IsBoolean = false,
    bool IsSecret = false,
    bool HasSecretValue = false,
    IReadOnlyList<string>? AllowedValues = null,
    bool CanManage = false,
    SettingModificationDto? UserModification = null,
    SettingModificationDto? TenantModification = null,
    bool IsCheckable = false,
    SettingCheckResultDto? LastCheck = null);

/// <summary>一项设置测试最近一次的结果。</summary>
/// <param name="Passed">是否通过。</param>
/// <param name="FailureCode">未通过时的失败码，见 <see cref="Checks.SettingCheckFailureCodes"/>；界面按码取文案，未知码走兜底。</param>
/// <param name="Time">测试时间（UTC）。</param>
/// <param name="CheckerId">测试人标识；无主体时为 <see langword="null"/>。</param>
/// <param name="CheckerName">测试人显示名快照；无主体时为 <see langword="null"/>。</param>
/// <param name="IsStale">测试之后主设置或依赖设置被写入或清除过，结论可能已不反映当前配置。</param>
public record SettingCheckResultDto(
    bool Passed,
    string? FailureCode,
    DateTime Time,
    string? CheckerId,
    string? CheckerName,
    bool IsStale)
{
    internal static SettingCheckResultDto From(Checks.SettingCheckResult result)
        => new(result.Passed, result.FailureCode, result.Time, result.CheckerId, result.CheckerName, result.IsStale);
}

/// <summary>某一层设置值最后一次写入的时间与写入人。</summary>
/// <param name="Time">写入时间（UTC）。</param>
/// <param name="ModifierId">写入人标识；无主体写入（迁移、种子、服务账号）时为 <see langword="null"/>。</param>
/// <param name="ModifierName">写入人显示名，取写入当时的快照；无主体写入时为 <see langword="null"/>。</param>
public record SettingModificationDto(DateTime Time, string? ModifierId, string? ModifierName);

/// <summary>写入一项设置。</summary>
/// <param name="Name">设置名称。</param>
/// <param name="Value">设置值；<see langword="null"/> 表示清除该层级的值，回落到下一层。</param>
public record SetSettingInputDto(string Name, string? Value);

/// <summary>一个租户的共享设置生效值，由 <c>MapSharedSettings</c> 映射的端点返回。</summary>
/// <remarks>
/// 只含标记了 <see cref="Definitions.ISettingDefinition.IsShared"/> 的设置；值为租户层的值，没有时为代码默认值，可以是 <see langword="null"/>。
/// 两端都按 <c>JsonSerializerDefaults.Web</c> 序列化，不随宿主的 JSON 配置变化。
/// </remarks>
/// <param name="Values">设置名 → 生效值。</param>
public record SharedSettingsDto(IReadOnlyDictionary<string, string?> Values)
{
    /// <summary>端点路径里租户段的宿主取值；其他取值为租户 Id。</summary>
    public const string HostSegment = "host";
}
