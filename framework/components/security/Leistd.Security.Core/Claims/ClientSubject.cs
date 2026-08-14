namespace Leistd.Security.Claims;

/// <summary>
/// 机器主体（OAuth2 client credentials 令牌代表的工作负载）的 <c>sub</c> 契约。
/// </summary>
/// <remarks>
/// 自然人主体的 <c>sub</c> 是用户 Id（GUID），解析方按 <c>Guid.TryParse</c> 认领。
/// 若机器主体直接把 <c>client_id</c> 写进 <c>sub</c>，两者就共享同一命名空间——
/// 而 <c>client_id</c> 由客户端创建者任意指定，挑一个已存在的用户 Id 即可让机器令牌
/// 被解析成那个人，继承其授予、角色乃至超管身份。加前缀后 <c>Guid.TryParse</c> 必然失败，
/// 这条冒充路径在结构上不存在，不依赖对 <c>client_id</c> 取值的任何输入校验。
///
/// 签发端（认证服务）按 <see cref="Format"/> 构造 <c>sub</c>，
/// 消费端（如服务间调用的用户上下文恢复）按 <see cref="Matches"/> 判定，契约只此一处。
/// </remarks>
public static class ClientSubject
{
    /// <summary>
    /// 机器主体 <c>sub</c> 的前缀。
    /// </summary>
    public const string Prefix = "client:";

    /// <summary>
    /// 由 <c>client_id</c> 构造机器主体的 <c>sub</c> 值。
    /// </summary>
    /// <param name="clientId">客户端标识</param>
    public static string Format(string clientId) => Prefix + clientId;

    /// <summary>
    /// 判断 <paramref name="subject"/> 是否正是 <paramref name="clientId"/> 对应的机器主体 <c>sub</c>。
    /// </summary>
    /// <param name="subject">待判定的 <c>sub</c> 值（可空）</param>
    /// <param name="clientId">客户端标识（可空）</param>
    /// <returns>两者均非空且完全匹配时为 <c>true</c></returns>
    public static bool Matches(string? subject, string? clientId) =>
        !string.IsNullOrEmpty(subject) &&
        !string.IsNullOrEmpty(clientId) &&
        string.Equals(subject, Format(clientId), StringComparison.Ordinal);
}
