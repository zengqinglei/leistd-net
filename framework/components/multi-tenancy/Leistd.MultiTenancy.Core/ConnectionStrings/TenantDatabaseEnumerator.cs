using Leistd.Data.Connections;
using Leistd.MultiTenancy.Abstractions;

namespace Leistd.MultiTenancy.ConnectionStrings;

// 清单取自库目录（不含连接串），再与宿主库合并。
//
// **判"要不要逐库走"只认 TenantConnectionRouting 这个显式标记**，不推断。
// 推断过两次，两次都是代理变量：先拿"有没有库目录"（目录回归控制库存储后，只承担控制面、
// 自己不分库的服务照样有它），再拿"有没有 IConnectionStringResolver"（那个契约写明
// 与多租户无关，随包文档就示范了单库实现）。标记由两个租户路由入口各放一份，说的就是这件事本身。
//
// 有标记却缺解析器、目录或租户上下文是**组合不完整**，必须抛。它和"单库"在这里长得一样
// （都没有独立库可列），后果却相反：那是把每一个独立库永久跳过，而归档、清理照样报成功。
//
// **宿主库必须参与去重。** 宿主连接与某个租户显式登记的连接可能指向同一个库；
// 两者用同一套指纹算法算出来就会相等，合并成一项。不合并的话，归档、清理这类
// 逐库作业会在同一个物理库上执行两遍。
internal sealed class TenantDatabaseEnumerator(
    TenantConnectionRouting? routing = null,
    ITenantDatabaseDirectory? directory = null,
    IConnectionStringResolver? connectionStringResolver = null,
    ICurrentTenant? currentTenant = null)
    : ITenantDatabaseEnumerator
{
    public async Task<TenantDatabaseSet> GetDatabasesAsync(
        string name,
        bool activeOnly,
        CancellationToken cancellationToken = default)
    {
        // 单库模式：本进程没有租户路由，所有租户都在宿主库里。宿主自己注册的通用
        // IConnectionStringResolver（按配置、按密钥服务取连接串）也走这一支——它与多租户无关。
        // 指纹在这一支不参与任何判定，留空串只是让形状一致。
        if (routing is null)
        {
            return new TenantDatabaseSet([TenantDatabase.ForHost(string.Empty)], []);
        }

        if (connectionStringResolver is null || directory is null || currentTenant is null)
        {
            throw new InvalidOperationException(
                $"Tenant connection routing is registered, but enumerating the databases for '{name}' also needs "
                + $"{nameof(IConnectionStringResolver)}, {nameof(ITenantDatabaseDirectory)} and {nameof(ICurrentTenant)}. "
                + "Without them every dedicated database would be skipped while per-database jobs report success.");
        }

        var hostFingerprint = await ResolveHostFingerprintAsync(name, cancellationToken);
        var listed = await directory.GetDatabasesAsync(name, activeOnly, cancellationToken);

        // 指纹等于宿主的那些登记直接丢掉：那就是宿主库，已经由下面的宿主项代表。
        // 不丢的话同一个物理库出现两次，归档、清理会在它上面跑两遍。
        // 丢掉不丢数据——进那个库用宿主配置即可，不需要经由某个租户。
        var dedicated = listed.Databases
            .Where(entry => !string.Equals(entry.Fingerprint, hostFingerprint, StringComparison.Ordinal))
            .Select(entry => new TenantDatabase(
                entry.TenantIds.Count > 0 ? entry.TenantIds[0] : null,
                entry.Fingerprint,
                entry.TenantIds));

        return new TenantDatabaseSet(
            [TenantDatabase.ForHost(hostFingerprint), .. dedicated],
            listed.FailedTenants);
    }

    // 宿主连接必须在宿主视角下解析：解析器是租户感知的，带着租户上下文调会拿到那个租户的库。
    private async Task<string> ResolveHostFingerprintAsync(string name, CancellationToken cancellationToken)
    {
        using (currentTenant!.Change(null))
        {
            var connectionString = await connectionStringResolver!.ResolveAsync(name, cancellationToken);
            return TenantDatabaseFingerprint.Of(connectionString);
        }
    }
}
