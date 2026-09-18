namespace Leistd.MultiTenancy.ConnectionStrings;

/// <summary>
/// 按连接名读取租户连接。
/// </summary>
/// <remarks>
/// <para>同一个契约表达两种宿主形态，按注册方式二选一：</para>
/// <list type="bullet">
/// <item>自己持有控制库的服务用 EF 实现（<c>AddMultiTenancyEfCore</c>），直接读库并解密连接串；</item>
/// <item>资源服务由宿主实现 HTTP 版本，向控制面回源。框架不认识任何具体服务的 HTTP 接口：
/// 路由、鉴权方式与响应形态归控制面服务所有，宿主用自己的客户端（如 Refit 接口）实现本契约。
/// 控制面经已认证的内部接口下发<b>已解密</b>的连接串，本服务不持有控制面的密钥环。</item>
/// </list>
/// <para><b>接口按名字问、按名字答，一次只出一条</b>：远端形态下，把整租户的连接列表交给某一个资源服务，
/// 等于把别的服务的库口令也发过去了。"精确名 → 默认名"的回落由实现完成。</para>
/// <para>两种实现不能同时注册：谁是权威由注册方式决定，<c>AddMultiTenancyEfCore</c> 在注册期断言这一点。</para>
/// <para>远端实现只在远端解析的共享任务里、于该任务自己的服务作用域内被解析，可以注册为 Scoped，
/// 但不能依赖发起请求的作用域（如 <c>HttpContext</c>）。调用失败必须抛异常；
/// 返回的 <see cref="TenantConnectionLookupResult.TenantId"/> 会与请求的租户比对，
/// 不一致时解析失败，不会被缓存或使用。实现不得记录响应里的连接串。</para>
/// </remarks>
public interface ITenantConnectionConfigurationStore
{
    /// <summary>
    /// 读取租户在指定连接名下的连接。
    /// </summary>
    /// <param name="tenantId">租户标识</param>
    /// <param name="name">连接名，通常是使用方 DbContext 的 <c>[ConnectionStringName]</c>；大小写不敏感</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>
    /// 查询结果；<see langword="null"/> 表示租户不存在或已删除，调用方必须失败关闭。
    /// 租户存在时一定返回非空结果，由 <see cref="TenantConnectionLookupResult.HasAnyConnection"/>
    /// 区分"不分库"与"缺这个名字"。
    /// </returns>
    Task<TenantConnectionLookupResult?> FindAsync(
        Guid tenantId,
        string name,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 枚举所有登记了连接的未删除租户在该名字下解析出的连接，供 DbMigrator 计算迁移目标。
    /// </summary>
    /// <remarks>
    /// 一条连接都没有的租户不出现——它们跟着宿主自己的库迁移。
    /// 登记过连接却解析不出这个名字的租户必须让调用方失败，而不是被跳过：
    /// 跳过的库会停在旧结构上，下一次发版才炸。
    /// </remarks>
    /// <param name="name">连接名；大小写不敏感</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task<IReadOnlyList<TenantMigrationConnection>> GetListAsync(
        string name,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 某个租户在某个连接名下解析出的连接。
/// </summary>
/// <param name="TenantId">租户标识</param>
/// <param name="Name">实际命中的连接名（精确名或默认名）</param>
/// <param name="ConnectionString">连接串（明文）；<see cref="ToString"/> 不输出它</param>
/// <remarks>
/// 带上 <paramref name="Name"/> 是为了能回答"这个租户为什么迁到了这个库"：
/// 名字本身不是敏感信息，而缺了它就分不清命中的是精确名还是默认名回落。
/// </remarks>
public sealed record TenantMigrationConnection(Guid TenantId, string Name, string ConnectionString)
{
    /// <inheritdoc />
    public override string ToString() =>
        $"{nameof(TenantMigrationConnection)} {{ TenantId = {TenantId}, Name = {Name} }}";
}
