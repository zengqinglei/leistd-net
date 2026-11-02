using Leistd.Data.Paging;
using Leistd.Webhooks.Dtos;

namespace Leistd.Webhooks.Management;

/// <summary>webhook 后台管理用例：事件类型、订阅的增删改与启停、密钥轮换、测试事件、投递日志与手动重发。</summary>
/// <remarks>
/// <para>只作用于当前租户（宿主上下文下为宿主）的订阅与投递，不接受租户参数。</para>
/// <para>不做权限检查——由端点策略决定。带入参的用例先按 DataAnnotations 校验，失败抛
/// <see cref="System.ComponentModel.DataAnnotations.ValidationException"/>，此时没有任何写入。</para>
/// <para>订阅或投递不存在时抛带 <see cref="Errors.WebhookErrorCodes.SubscriptionNotFound"/> 或
/// <see cref="Errors.WebhookErrorCodes.DeliveryNotFound"/> 的业务异常。</para>
/// <para>写操作自带工作单元；投递类写操作（测试事件、手动重发、带 <c>retryFailed</c> 的启用）在提交后立即入队发送。</para>
/// <para>创建、修改、删除、轮换密钥、启用与停用成功后经可选的 <c>ILocalEventBus</c> 发布
/// <see cref="Events.WebhookSubscriptionChangedEvent"/>，提交后分发；状态未变的再启用、再停用不发布。</para>
/// </remarks>
public interface IWebhookManagementService
{
    /// <summary>获取业务登记的事件类型，按登记顺序；不含内置的测试事件 <see cref="Definitions.WebhookDefinition.TestEventType"/>，它只经 <see cref="SendTestEventAsync"/> 发送。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<IReadOnlyList<WebhookEventTypeOutputDto>> GetEventTypesAsync(CancellationToken cancellationToken = default);

    /// <summary>分页查询订阅，按创建时间倒序；每项带最近一次投递与近 7 天成功率。</summary>
    /// <param name="input">关键字、启用状态、事件类型与分页；<see cref="PageRequest.Sorting"/> 不生效。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<PagedResult<WebhookSubscriptionOutputDto>> GetSubscriptionsAsync(
        GetWebhookSubscriptionsInputDto input,
        CancellationToken cancellationToken = default);

    /// <summary>获取订阅详情。</summary>
    /// <param name="id">订阅标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<WebhookSubscriptionOutputDto> GetSubscriptionAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>创建启用的订阅并生成密钥。</summary>
    /// <param name="input">创建入参；地址须是绝对 http(s) URL，事件类型须是业务登记的类型（不含测试事件），或单独的 <see cref="Definitions.WebhookDefinition.AllEventTypes"/>。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>订阅与一次性明文密钥。</returns>
    Task<WebhookSubscriptionSecretOutputDto> CreateSubscriptionAsync(
        CreateWebhookSubscriptionInputDto input,
        CancellationToken cancellationToken = default);

    /// <summary>更新名称、地址、事件类型与自定义请求头。</summary>
    /// <param name="id">订阅标识。</param>
    /// <param name="input">新值；全部覆盖。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<WebhookSubscriptionOutputDto> UpdateSubscriptionAsync(
        Guid id,
        UpdateWebhookSubscriptionInputDto input,
        CancellationToken cancellationToken = default);

    /// <summary>删除订阅，并一并删除它的投递与尝试；它们随之退出统计。</summary>
    /// <param name="id">订阅标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task DeleteSubscriptionAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>启用订阅：清空停用原因、停用时刻与失败起点；已启用时不变。</summary>
    /// <param name="id">订阅标识。</param>
    /// <param name="retryFailed">
    /// 为 <see langword="true"/> 时，该订阅现存（保留期内）的全部失败投递各按手动重发处理一次；
    /// 停用期间发生的事件不会补发。
    /// </param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<WebhookSubscriptionOutputDto> EnableSubscriptionAsync(
        Guid id,
        bool retryFailed = false,
        CancellationToken cancellationToken = default);

    /// <summary>手动停用订阅：同一事务里把它待投递的记录改为失败；已停用时不变。</summary>
    /// <remarks>停用提交后分发 <see cref="Events.WebhookSubscriptionChangedEvent"/>（<see cref="Events.WebhookSubscriptionChangeKind.Disabled"/>，原因 <see cref="WebhookDisabledReason.Manual"/>）。已发出的请求不取消。</remarks>
    /// <param name="id">订阅标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<WebhookSubscriptionOutputDto> DisableSubscriptionAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>轮换密钥：生成新密钥，旧密钥在 <c>Leistd:Webhooks:SecretRotationOverlap</c> 内继续一并签名。</summary>
    /// <param name="id">订阅标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>订阅与一次性明文新密钥。</returns>
    Task<WebhookSubscriptionSecretOutputDto> RotateSecretAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>向该订阅发一条 <see cref="Definitions.WebhookDefinition.TestEventType"/> 事件，不论它是否订阅了该类型。</summary>
    /// <param name="id">订阅标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>新建的投递。</returns>
    /// <exception cref="ExceptionHandling.BusinessException">订阅已停用（<see cref="Errors.WebhookErrorCodes.SubscriptionDisabled"/>）。</exception>
    Task<WebhookDeliveryOutputDto> SendTestEventAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>分页查询投递，按创建时间倒序。</summary>
    /// <param name="input">关键字、订阅、状态、事件类型与分页；<see cref="PageRequest.Sorting"/> 不生效。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<PagedResult<WebhookDeliveryOutputDto>> GetDeliveriesAsync(
        GetWebhookDeliveriesInputDto input,
        CancellationToken cancellationToken = default);

    /// <summary>获取投递详情：载荷与全部尝试。</summary>
    /// <param name="id">投递标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<WebhookDeliveryDetailOutputDto> GetDeliveryAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>手动重发一次终态（成功或失败）的投递：事件标识不变，只发一次，失败不重开自动退避。</summary>
    /// <param name="id">投递标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <exception cref="ExceptionHandling.BusinessException">
    /// 投递仍在重试中（<see cref="Errors.WebhookErrorCodes.DeliveryNotRetryable"/>），或订阅已停用（<see cref="Errors.WebhookErrorCodes.SubscriptionDisabled"/>）。
    /// </exception>
    Task<WebhookDeliveryOutputDto> RetryDeliveryAsync(Guid id, CancellationToken cancellationToken = default);
}
