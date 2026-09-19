using System.Security.Cryptography;
using System.Text;

namespace Leistd.MultiTenancy.ConnectionStrings;

/// <summary>
/// 枚举租户登记的独立物理库，每个库一条。
/// </summary>
/// <remarks>
/// <para>"有哪些独立库"的唯一口径：迁移作业直接用它的连接串，运行时经
/// <see cref="ITenantDatabaseEnumerator"/> 取同一份清单（去掉连接串、补上宿主库）。
/// 去重规则只在这里，两边不各写一份。</para>
/// <para>直接调用仅限迁移作业（DbMigrator 一类的一次性进程）：结果带明文连接串，请求入口与后台作业应改用
/// <see cref="ITenantDatabaseEnumerator"/>。</para>
/// <para>目标使用租户在该连接名下的那一条连接串（运行时与迁移共用）；
/// 登记过连接却解析不出该名字时抛 <see cref="InvalidOperationException"/> 终止作业，而不是跳过该租户。</para>
/// </remarks>
public interface ITenantMigrationTargetProvider
{
    /// <summary>
    /// 读取全部登记了连接的租户在指定连接名下的物理库，同一连接串只出现一次。
    /// </summary>
    /// <remarks>
    /// 多个租户共用一个库时，<see cref="TenantMigrationTarget.TenantId"/> 取其中标识最小者，结果稳定；
    /// 库的顺序按首次出现。去重按连接串全文比较，写法不同但指向同一个库的连接串会各出现一次，
    /// 因此逐库逻辑（迁移本身即是）须能重复执行。
    /// </remarks>
    /// <param name="name">
    /// 连接名，通常是迁移作业所属服务的业务 DbContext 的 <c>[ConnectionStringName]</c>；大小写不敏感
    /// </param>
    /// <param name="cancellationToken">取消令牌</param>
    Task<IReadOnlyList<TenantMigrationTarget>> GetDedicatedTargetsAsync(
        string name,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 一个独立物理库目标。
/// </summary>
/// <param name="TenantId">代表租户：进入该库时切换到的租户；多个租户共用该库时取标识最小者</param>
/// <param name="ConnectionString">连接串（明文）；<see cref="ToString"/> 不输出它</param>
public sealed record TenantMigrationTarget(Guid TenantId, string ConnectionString)
{
    /// <summary>连接串的 SHA-256 指纹（十六进制），用于日志与去重，不暴露连接串本身。</summary>
    public string Fingerprint => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ConnectionString)));

    /// <inheritdoc />
    public override string ToString() => $"{nameof(TenantMigrationTarget)} {{ TenantId = {TenantId}, Fingerprint = {Fingerprint} }}";
}
