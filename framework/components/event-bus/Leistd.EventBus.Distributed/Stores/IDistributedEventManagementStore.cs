using Leistd.Data.Paging;
using Leistd.EventBus.Distributed.Dtos;

namespace Leistd.EventBus.Distributed.Stores;

// 管理分页读取的权威存储；按输入选择宿主物理库或当前租户，不在这里接受任意租户参数。
// 与发布、接收及维护共同由同一个存储适配包提供，不单独公开替换这一部分。
internal interface IDistributedEventManagementStore
{
    Task<PagedResult<DistributedEventOutboxOutputDto>> GetOutboxListAsync(
        GetDistributedEventOutboxInputDto input, CancellationToken cancellationToken);

    Task<PagedResult<DistributedEventInboxOutputDto>> GetInboxListAsync(
        GetDistributedEventInboxInputDto input, CancellationToken cancellationToken);

    Task<PagedResult<DistributedEventRejectionOutputDto>> GetRejectionsAsync(
        GetDistributedEventRejectionsInputDto input, CancellationToken cancellationToken);
}
