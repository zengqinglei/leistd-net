namespace Leistd.Settings.Stores;

/// <summary>某一层级上一项设置最后一次写入的时间与写入人。</summary>
/// <remarks>
/// 写入人存的是写入时的快照：资源服务没有用户表，事后拿标识换不出名字；改名后快照不随之变化，记录的是当时是谁。
/// 无主体的写入（迁移、种子、服务账号）两项为 <see langword="null"/>。
/// </remarks>
/// <param name="Time">写入时间（UTC）。</param>
/// <param name="ModifierId">写入人标识；无主体时为 <see langword="null"/>。</param>
/// <param name="ModifierName">写入人显示名快照；无主体时为 <see langword="null"/>。</param>
public sealed record SettingModification(DateTime Time, string? ModifierId, string? ModifierName);
