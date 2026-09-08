using Leistd.Notifications.Dtos;

namespace Leistd.Notifications.Abstractions;

/// <summary>
/// 通知外发渠道 —— 把已经定案的通知通过某一种介质（SignalR、邮件、短信、移动推送……）
/// 送出去，只管送达，不管持久化。
/// </summary>
/// <remarks>
/// <para><b>这不是业务入口，业务代码不要注入它。</b>发通知一律用
/// <see cref="INotificationPublisher"/>：它定案这条记录的身份、写入收件人历史，
/// 再把同一个对象交给每个渠道。直接调本接口的后果是"实时到达、刷新后铃铛空白、
/// 未读数不涨"，而且不报错——通知的定义里就包含历史与未读数。只要瞬态推送、
/// 不要历史的场景用 realtime 组件的 <c>IBusinessEventPublisher</c>，那是另一件事。</para>
/// <para>与发布器的区分写在名字里：发布器<b>发布</b>一条通知（一个用例），本接口
/// <b>投递</b>它（一种介质的实现）。两者的参数列表相同，但方法名不同——
/// 曾经同为 <c>...ToUserAsync</c> 时，注错一个不会有任何编译或运行期提示。</para>
/// <para>这是<b>累加型</b>扩展点：发布器以 <c>IEnumerable&lt;T&gt;</c> 注入并逐一 <c>await</c>，
/// 宿主可同时装 SignalR、邮件、移动推送等多个渠道。</para>
/// <para><b>送达失败照常抛出，不必自己吞。</b>跨渠道的隔离由发布器负责：它逐个渠道捕获、
/// 记日志、继续下一个——历史已经落库，一个渠道送不到不该让整次发布失败，也不该让后面的
/// 渠道收不到。把吞异常的责任交给每个实现，等于让这条保证取决于「每个实现者是否记得」。
/// 唯一要放过的是 <see cref="OperationCanceledException"/>：调用方主动取消应如实传播。</para>
/// </remarks>
public interface INotificationChannel
{
    /// <summary>把通知投递给指定用户。</summary>
    /// <param name="userId">收件人标识；须与该客户端的 SignalR <c>UserIdentifier</c> 一致。</param>
    /// <param name="notification">已由发布器定案的通知（含 <c>Id</c>、<c>CreationTime</c>）。</param>
    /// <param name="ct">取消令牌。</param>
    Task DeliverAsync(string userId, NotificationOutputDto notification, CancellationToken ct = default);
}
