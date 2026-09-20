namespace Leistd.Settings.Dtos;

/// <summary>
/// 一项设置在各层级上的覆盖值、可写层级与值元数据。
/// </summary>
/// <remarks>
/// 给的是各层级的原始覆盖值而不是回落后的生效值：设置页要分层编辑，
/// 只给生效值会让租户页显示出当前用户的个人偏好，一旦保存就把私人偏好写成了租户默认值。
/// <see langword="null"/> 表示该层未覆盖，界面据此把继承来的值渲染成占位符。
/// <para>层级用布尔量表达而不是透出 <c>SettingScopes</c> 位标志：宿主的 JSON 配置决定枚举按数字还是名称序列化，
/// 客户端的判断方式会随之而变。</para>
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
    IReadOnlyList<string>? AllowedValues = null);

/// <summary>
/// 写入一项设置。
/// </summary>
/// <param name="Name">设置名称。</param>
/// <param name="Value">设置值；<see langword="null"/> 表示清除该层级的值，回落到下一层。</param>
public record SetSettingInputDto(string Name, string? Value);
