using Leistd.Data.Paging;
using Leistd.EventBus.Distributed.Dtos;
namespace Leistd.EventBus.Distributed.Management;

/// <summary>分布式事件管理用例；按当前租户或宿主所选库操作，所有输入在此校验。</summary>
public interface IDistributedEventManagementService
{
    /// <summary>获取状态快照。</summary>
    Task<DistributedEventStatusOutputDto> GetStatusAsync(string? database = null, CancellationToken cancellationToken = default);

    /// <summary>列出宿主物理库；租户拒绝。</summary>
    Task<IReadOnlyList<DistributedEventDatabaseOutputDto>> GetDatabasesAsync(CancellationToken cancellationToken = default);

    /// <summary>获取事件目录。</summary>
    Task<IReadOnlyList<DistributedEventCatalogOutputDto>> GetCatalogAsync(string? database = null, CancellationToken cancellationToken = default);

    /// <summary>按 Id 倒序分页读取概要，不含载荷。</summary>
    Task<PagedResult<DistributedEventOutboxOutputDto>> GetOutboxListAsync(GetDistributedEventOutboxInputDto input, CancellationToken cancellationToken = default);

    /// <summary>读取详情概要；不存在时抛 RecordNotFound。</summary>
    Task<DistributedEventOutboxDetailOutputDto> GetOutboxAsync(long id, string? database = null, CancellationToken cancellationToken = default);

    /// <summary>读取冻结载荷，须由 Manage 策略授权。</summary>
    Task<DistributedEventPayloadOutputDto> GetOutboxPayloadAsync(long id, string? database = null, CancellationToken cancellationToken = default);

    /// <summary>按 Id 倒序分页读取概要，不含载荷。</summary>
    Task<PagedResult<DistributedEventInboxOutputDto>> GetInboxListAsync(GetDistributedEventInboxInputDto input, CancellationToken cancellationToken = default);

    /// <summary>读取详情概要；不存在时抛 RecordNotFound。</summary>
    Task<DistributedEventInboxDetailOutputDto> GetInboxAsync(long id, string? database = null, CancellationToken cancellationToken = default);

    /// <summary>读取冻结载荷，须由 Manage 策略授权。</summary>
    Task<DistributedEventPayloadOutputDto> GetInboxPayloadAsync(long id, string? database = null, CancellationToken cancellationToken = default);

    /// <summary>按 Id 倒序分页读取概要，不含载荷。</summary>
    Task<PagedResult<DistributedEventRejectionOutputDto>> GetRejectionsAsync(GetDistributedEventRejectionsInputDto input, CancellationToken cancellationToken = default);

    /// <summary>读取详情概要；不存在时抛 RecordNotFound。</summary>
    Task<DistributedEventRejectionDetailOutputDto> GetRejectionAsync(long id, string? database = null, CancellationToken cancellationToken = default);

    /// <summary>读取冻结载荷，须由 Manage 策略授权。</summary>
    Task<DistributedEventRawMessageOutputDto> GetRejectionPayloadAsync(long id, string? database = null, CancellationToken cancellationToken = default);

    /// <summary>重投死信，重置发送失败窗口；筛选动作分批提交。</summary>
    Task<DistributedEventRecoveryOutputDto> RedeliverAsync(RecoverDistributedEventOutboxInputDto input, string? database = null, CancellationToken cancellationToken = default);

    /// <summary>重放 Processed 或 Failed，清零本轮次数；Processed 会再次执行处理器。</summary>
    Task<DistributedEventRecoveryOutputDto> ReplayAsync(RecoverDistributedEventInboxInputDto input, string? database = null, CancellationToken cancellationToken = default);

    /// <summary>重新解码宿主拒收；没有同名本地传输时转交 Worker，DeferredCount 标识待办数量。</summary>
    Task<DistributedEventRecoveryOutputDto> RedecodeAsync(RecoverDistributedEventRejectionInputDto input, string? database = null, CancellationToken cancellationToken = default);

    /// <summary>将宿主 Pending 拒收标记为 Discarded。</summary>
    Task<DistributedEventRecoveryOutputDto> DiscardAsync(RecoverDistributedEventRejectionInputDto input, string? database = null, CancellationToken cancellationToken = default);

    /// <summary>在当前上下文的工作单元内发布测试事件；不允许通过库选择冒充租户。</summary>
    Task<DistributedEventTestOutputDto> SendTestEventAsync(string? database = null, CancellationToken cancellationToken = default);
}
