namespace Leistd.Notifications.Email.Abstractions;

/// <summary>
/// 把通知收件人解析成可投递的邮件地址，由宿主实现（它才知道用户模型）。
/// </summary>
public interface INotificationRecipientResolver
{
    /// <summary>
    /// 返回收件人可投递的邮件地址；没有地址或地址未经验证时返回 <see langword="null"/>，该用户这一次不发邮件。
    /// </summary>
    /// <remarks>只返回验证过的地址：发到未验证的地址等于把通知内容交给一个可能不属于该用户的邮箱。</remarks>
    /// <param name="userId">收件人。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<string?> ResolveEmailAsync(string userId, CancellationToken cancellationToken = default);
}
