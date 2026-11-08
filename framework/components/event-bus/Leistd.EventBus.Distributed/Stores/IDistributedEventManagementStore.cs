using Leistd.EventBus.Distributed.Management;

namespace Leistd.EventBus.Distributed.Stores;

// 管理读写由一个持久化适配器提供；复用管理 DTO，不另造相同的记录、分页与恢复结果模型。
internal interface IDistributedEventManagementStore : IDistributedEventManagementService
{
}
