namespace CompanyName.ProjectName.Application.Settings.Dtos;

/// <summary>
/// 一项设置在各层级上的覆盖值及其可覆盖层级。
/// </summary>
/// <remarks>
/// 给的是各层级的原始覆盖值而不是回落后的生效值：设置页要分层编辑，
/// 只给生效值会让租户页显示出当前用户的个人偏好，一旦保存就把私人偏好写成了租户默认值。
/// <see langword="null"/> 表示该层未覆盖，界面据此把继承来的值渲染成占位符。
/// <para>
/// 层级用两个布尔量表达，而不是直接透出 <c>SettingScopes</c> 位标志：宿主的 JSON 配置决定
/// 枚举按数字还是名称序列化，客户端要么按位运算、要么按名称匹配，两种都会随宿主配置而变。
/// </para>
/// </remarks>
/// <param name="Name">设置名称。</param>
/// <param name="DisplayName">已按请求 culture 翻译的显示名称。</param>
/// <param name="UserValue">当前用户的个人覆盖值；<see langword="null"/> 表示未覆盖。</param>
/// <param name="TenantValue">当前租户的默认覆盖值；<see langword="null"/> 表示未覆盖。</param>
/// <param name="DefaultValue">代码默认值。</param>
/// <param name="AllowsTenantScope">是否允许写入租户级默认值。</param>
/// <param name="AllowsUserScope">是否允许用户覆盖为个人偏好。</param>
public record SettingOutputDto(
    string Name,
    string DisplayName,
    string? UserValue,
    string? TenantValue,
    string? DefaultValue,
    bool AllowsTenantScope,
    bool AllowsUserScope);

/// <summary>
/// 写入一项设置。
/// </summary>
/// <param name="Name">设置名称。</param>
/// <param name="Value">设置值；<see langword="null"/> 表示清除该层级的值，回落到下一层。</param>
public record SetSettingInputDto(string Name, string? Value);
