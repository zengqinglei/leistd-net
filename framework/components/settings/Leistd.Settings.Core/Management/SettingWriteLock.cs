using Leistd.MultiTenancy.Context;

namespace Leistd.Settings.Management;

// 同一租户的设置写入与测试结果的保存共用一把锁：保存前核对依赖值时，并发写入要么已提交、要么还没开始
internal static class SettingWriteLock
{
    public static string Key(ICurrentTenant? currentTenant)
        => currentTenant?.Id is { } tenantId ? $"settings:{tenantId:D}" : "settings:host";
}
