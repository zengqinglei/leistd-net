namespace Leistd.Settings.ServiceClient.Reading;

/// <summary>共享设置值的来源。</summary>
public enum SharedSettingSource
{
    /// <summary>新鲜期内或刚从提供方确认的值。</summary>
    Current,

    /// <summary>提供方暂不可达，用的是保留期内的旧值。</summary>
    Stale,

    /// <summary>读不到（没有可用的旧值、提供方拒绝或对方没有共享这一项），用的是使用方声明的兜底值。</summary>
    Fallback,
}
