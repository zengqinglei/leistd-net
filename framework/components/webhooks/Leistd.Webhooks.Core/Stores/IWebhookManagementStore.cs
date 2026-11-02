using Leistd.Data.Paging;
using Leistd.Webhooks.Delivery;
using Leistd.Webhooks.Dtos;
using Leistd.Webhooks.Management;

namespace Leistd.Webhooks.Stores;

// 管理用例的存储契约：只含管理操作实际要的读写，校验、启停判定、密钥生成、重发判定、变更事件与输出组装都在用例里。
// 租户隔离由实现按当前租户上下文与数据过滤器承担，本契约不带租户参数；找不到（含别的租户的）返回 null。
// 写入方法只改当前工作单元里的数据，由用例保存与提交。不公开：发布、投递与保留期都绑在同一个存储实现上，
// 单换这一层不构成可替换的存储。
internal interface IWebhookManagementStore
{
    // 订阅分页：关键字匹配名称或地址（大小写不敏感），事件类型匹配时订阅了全部事件的也算；新建的在前。
    Task<PagedResult<WebhookSubscriptionEntry>> GetSubscriptionsAsync(GetWebhookSubscriptionsInputDto input, CancellationToken cancellationToken);

    Task<WebhookSubscriptionEntry?> FindSubscriptionAsync(Guid id, CancellationToken cancellationToken);

    // 这些订阅各自的最近一次投递，及 since 之后创建的投递里当前成功、失败的条数；没有投递的订阅不在结果里。
    Task<IReadOnlyDictionary<Guid, WebhookDeliverySummary>> GetDeliverySummariesAsync(
        IReadOnlyCollection<Guid> subscriptionIds,
        DateTime since,
        CancellationToken cancellationToken);

    // 写入当前租户的新订阅，启用状态。
    Task<WebhookSubscriptionEntry> AddSubscriptionAsync(
        WebhookSubscriptionFields fields,
        string protectedSecret,
        DateTime creationTime,
        CancellationToken cancellationToken);

    Task<WebhookSubscriptionEntry?> UpdateSubscriptionAsync(Guid id, WebhookSubscriptionFields fields, CancellationToken cancellationToken);

    // 已启用时不改；Changed 表示这次调用确实把它从停用改成了启用，并清掉停用与连续失败的记录。
    Task<(WebhookSubscriptionEntry Subscription, bool Changed)?> EnableSubscriptionAsync(Guid id, CancellationToken cancellationToken);

    // 按"停用的作用"停用并终结待投递；已停用时不改，Changed 为 false。
    Task<(WebhookSubscriptionEntry Subscription, bool Changed)?> DisableSubscriptionAsync(
        Guid id,
        WebhookDisabledReason reason,
        CancellationToken cancellationToken);

    // 换上新密钥；previousSecretExpiresAt 有值时当前密钥转为旧密钥、到该时刻为止仍参与签名，否则不保留旧密钥。
    Task<WebhookSubscriptionEntry?> RotateSecretAsync(
        Guid id,
        string protectedSecret,
        DateTime? previousSecretExpiresAt,
        CancellationToken cancellationToken);

    // 删除订阅，连带它的投递与尝试；返回删除前的订阅。
    Task<WebhookSubscriptionEntry?> DeleteSubscriptionAsync(Guid id, CancellationToken cancellationToken);

    // 把该订阅现存的全部失败投递排为手动重发，提交后入队；返回排上的条数。
    Task<int> RetryFailedDeliveriesAsync(Guid subscriptionId, CancellationToken cancellationToken);

    // 写一条事件并只投给这一个订阅，不经发布器的类型匹配；提交后入队。
    Task<WebhookDeliveryEntry> AddDeliveryAsync(
        WebhookSubscriptionEntry subscription,
        string eventType,
        object? data,
        CancellationToken cancellationToken);

    // 投递分页：关键字按事件标识整值或事件类型包含匹配；新建的在前。
    Task<PagedResult<WebhookDeliveryEntry>> GetDeliveriesAsync(GetWebhookDeliveriesInputDto input, CancellationToken cancellationToken);

    Task<WebhookDeliveryEntry?> FindDeliveryAsync(Guid id, CancellationToken cancellationToken);

    // 投递连同事件载荷与全部尝试（新的在前）。
    Task<WebhookDeliveryDetailEntry?> GetDeliveryDetailAsync(Guid id, CancellationToken cancellationToken);

    // 把一条终态投递排为手动重发，提交后入队；是否允许由调用方先判断。
    Task<WebhookDeliveryEntry?> ScheduleManualRetryAsync(Guid id, CancellationToken cancellationToken);
}

// 订阅可编辑的部分，已由用例规范化。
internal sealed record WebhookSubscriptionFields(
    string Name,
    string? Description,
    string Url,
    IReadOnlyList<string> EventTypes,
    IReadOnlyDictionary<string, string> Headers);

// 订阅的当前存储状态；Secret 是 Data Protection 密文。
internal sealed record WebhookSubscriptionEntry
{
    public required Guid Id { get; init; }

    public Guid? TenantId { get; init; }

    public required string Name { get; init; }

    public string? Description { get; init; }

    public required string Url { get; init; }

    public required IReadOnlyList<string> EventTypes { get; init; }

    public required IReadOnlyDictionary<string, string> Headers { get; init; }

    public required string Secret { get; init; }

    public DateTime? PreviousSecretExpiresAt { get; init; }

    public required bool IsEnabled { get; init; }

    public WebhookDisabledReason? DisabledReason { get; init; }

    public DateTime? DisabledAt { get; init; }

    public DateTime? FirstFailureAt { get; init; }

    public required DateTime CreationTime { get; init; }
}

// 一个订阅的投递摘要：窗口内当前成功、失败的条数与最近一次投递。
internal sealed record WebhookDeliverySummary(int Succeeded, int Failed, WebhookSubscriptionLastDeliveryDto? Last);

// 投递的当前存储状态，带事件类型与所属订阅的名称、地址、启用状态。
internal sealed record WebhookDeliveryEntry
{
    public required Guid Id { get; init; }

    public required Guid EventId { get; init; }

    public required string EventType { get; init; }

    public required Guid SubscriptionId { get; init; }

    public required string SubscriptionName { get; init; }

    public required string SubscriptionUrl { get; init; }

    public required bool SubscriptionIsEnabled { get; init; }

    public required WebhookDeliveryStatus Status { get; init; }

    public required int AttemptCount { get; init; }

    public int? LastStatusCode { get; init; }

    public string? LastError { get; init; }

    public DateTime? LastAttemptAt { get; init; }

    public DateTime? NextAttemptAt { get; init; }

    public required WebhookAttemptTrigger NextTrigger { get; init; }

    public required DateTime CreationTime { get; init; }
}

internal sealed record WebhookDeliveryDetailEntry(WebhookDeliveryEntry Delivery, string Payload, IReadOnlyList<WebhookAttemptEntry> Attempts);

internal sealed record WebhookAttemptEntry
{
    public required Guid Id { get; init; }

    public required DateTime AttemptedAt { get; init; }

    public required WebhookAttemptTrigger Trigger { get; init; }

    public required IReadOnlyDictionary<string, string> RequestHeaders { get; init; }

    public int? StatusCode { get; init; }

    public required long DurationMs { get; init; }

    public string? Response { get; init; }

    public string? Error { get; init; }
}
