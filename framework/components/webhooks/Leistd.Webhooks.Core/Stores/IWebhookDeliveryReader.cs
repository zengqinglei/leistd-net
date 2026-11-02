namespace Leistd.Webhooks.Stores;

/// <summary>投递统计的读取契约，供宿主按自己的日历区间组装统计。</summary>
/// <remarks>
/// <para>只读当前租户层：租户隔离由实现按当前租户上下文与数据过滤器承担，因此本契约不带租户参数。</para>
/// <para>组件不接收时区、不生成日历；桶怎么切、上一区间怎么取由调用方决定。已删除的订阅连同其投递退出统计。</para>
/// </remarks>
public interface IWebhookDeliveryReader
{
    /// <summary>按调用方给定的 UTC 桶边界，统计每个桶内创建的投递数及其当前状态分布。</summary>
    /// <remarks>
    /// <para>第 i 个结果对应 UTC 半开区间 [<paramref name="edges"/>[i], <paramref name="edges"/>[i+1])：恰好落在边界上的投递
    /// 只归后一个桶；没有投递的桶各项为 0，结果长度恒为边界数减一。桶可以不等长。</para>
    /// <para>投递按创建时刻（即发布时刻）分桶，按当前状态计数：手动重发改变终态后，统计随之改变。</para>
    /// </remarks>
    /// <param name="edges">桶边界，UTC；至少两个且严格递增，首尾即统计区间。</param>
    /// <param name="subscriptionId">只统计该订阅的投递；<see langword="null"/> 表示全部订阅。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <exception cref="ArgumentException">边界少于两个，或不是严格递增。</exception>
    Task<IReadOnlyList<WebhookDeliveryCounts>> CountByBucketsAsync(
        IReadOnlyList<DateTime> edges,
        Guid? subscriptionId = null,
        CancellationToken cancellationToken = default);

    /// <summary>读取当前存量：重试中的投递数与已停用的订阅数，不随统计区间变化。</summary>
    /// <param name="subscriptionId">只统计该订阅；<see langword="null"/> 表示全部订阅。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<WebhookDeliverySnapshot> GetSnapshotAsync(Guid? subscriptionId = null, CancellationToken cancellationToken = default);

    /// <summary>保留期下限（UTC）：早于它创建的投递可能已被清理；未启用保留期时为 <see langword="null"/>。</summary>
    /// <remarks>按当前时刻与当前保留天数计算，不访问存储。统计据此判断上一区间是否完整：区间起点早于下限时不提供可比数据。</remarks>
    DateTime? GetRetainedSince();
}

/// <summary>一个统计桶内创建的投递，按当前状态计数。</summary>
/// <param name="Total">投递数；一个事件发给 N 个订阅计 N。</param>
/// <param name="Succeeded">当前为成功的投递数。</param>
/// <param name="Failed">当前为失败的投递数。</param>
/// <param name="Pending">当前仍待投递（重试中）的投递数。</param>
public sealed record WebhookDeliveryCounts(int Total, int Succeeded, int Failed, int Pending);

/// <summary>投递与订阅的当前存量。</summary>
/// <param name="PendingDeliveries">当前仍待投递（重试中）的投递数。</param>
/// <param name="DisabledSubscriptions">已停用的订阅数；按订阅统计时为 0 或 1。</param>
public sealed record WebhookDeliverySnapshot(int PendingDeliveries, int DisabledSubscriptions);
