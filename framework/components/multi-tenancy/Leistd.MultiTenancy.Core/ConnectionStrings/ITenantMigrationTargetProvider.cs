using System.Security.Cryptography;
using System.Text;

namespace Leistd.MultiTenancy.ConnectionStrings;

/// <summary>
/// 枚举需要施加迁移的租户库目标。
/// </summary>
/// <remarks>
/// 仅供迁移作业（DbMigrator 一类的一次性进程）调用，请求入口不应使用。
/// 目标使用租户在该连接名下的那一条连接串（运行时与迁移共用）；
/// 登记过连接却解析不出该名字时抛 <see cref="InvalidOperationException"/> 终止作业，而不是跳过该租户。
/// </remarks>
public interface ITenantMigrationTargetProvider
{
    /// <summary>
    /// 读取全部登记了连接的租户在指定连接名下的迁移目标。
    /// </summary>
    /// <param name="name">
    /// 连接名，通常是迁移作业所属服务的业务 DbContext 的 <c>[ConnectionStringName]</c>；大小写不敏感
    /// </param>
    /// <param name="cancellationToken">取消令牌</param>
    Task<IReadOnlyList<TenantMigrationTarget>> GetDedicatedTargetsAsync(
        string name,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 一个租户库迁移目标。
/// </summary>
/// <param name="TenantId">租户标识</param>
/// <param name="ConnectionString">连接串（明文）；<see cref="ToString"/> 不输出它</param>
public sealed record TenantMigrationTarget(Guid TenantId, string ConnectionString)
{
    /// <summary>连接串的 SHA-256 指纹（十六进制），用于日志与去重，不暴露连接串本身。</summary>
    public string Fingerprint => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ConnectionString)));

    /// <inheritdoc />
    public override string ToString() => $"{nameof(TenantMigrationTarget)} {{ TenantId = {TenantId}, Fingerprint = {Fingerprint} }}";
}
