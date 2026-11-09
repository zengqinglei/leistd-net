namespace Leistd.Settings.ServiceClient.Reading;

/// <summary>一项共享设置的值及其来源。</summary>
/// <param name="Value">生效值；提供方的值或兜底值都可能是 <see langword="null"/>。</param>
/// <param name="Source">值从哪里来。</param>
public readonly record struct SharedSettingValue(string? Value, SharedSettingSource Source);
