using CompanyName.ProjectName.Application.OperationRecords.Dtos;
using Leistd.Ddd.Application.Contracts.AppService;
using Leistd.Ddd.Application.Contracts.Dtos;

namespace CompanyName.ProjectName.Application.OperationRecords.AppServices;

/// <summary>
/// 操作记录应用服务接口。
/// </summary>
/// <remarks>
/// <b>只读。</b>记录写下之后不再修改或删除——留了入口，"清理误记录"迟早会变成
/// "清理不想被看到的记录"，那时这张表已经不能作为证据了。保留策略属于运维范畴。
/// </remarks>
public interface IOperationRecordAppService : IAppService
{
    /// <summary>
    /// 按创建时间倒序分页查询当前租户的操作记录。
    /// </summary>
    Task<PagedResultDto<OperationRecordOutputDto>> GetPagedListAsync(
        GetOperationRecordPagedInputDto input,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 下发筛选项：有哪些类别、哪些动作码。
    /// </summary>
    /// <remarks>
    /// <b>选项必须由服务端下发，不能让界面硬编码动作码。</b>动作码由
    /// <c>IOperationActionDefinitionProvider</c> 登记，硬编码等于把那份登记复制一份到前端，
    /// 两处迟早漂移——而症状是"某个动作在筛选框里选不到"，没有任何报错。
    /// <para>选项按当前读者的可见性裁剪：租户读者拿不到 <c>Host</c> 层动作。</para>
    /// </remarks>
    Task<OperationRecordFilterOptionsOutputDto> GetFilterOptionsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 把当前筛选条件下的记录导出为 CSV。
    /// </summary>
    /// <remarks>
    /// <para><b>单独授权。</b>导出把审计数据整批带离系统，之后既不受可见性分层约束、
    /// 也不再有访问记录，影响面与在线翻页不是一个量级。导出动作本身会被审计
    /// （<c>operation-records.exported</c>）。</para>
    /// <para><b>与分页查询走同一条可见性路径</b>：记录级范围、动作码展开、字段级裁剪
    /// 三步都复用同一份实现。导出若另写一份，迟早与主路径漂移，症状是
    /// 「界面看不到的内容能被导出来」——那是越权，不是显示差异。</para>
    /// <para><b>不是全量导出。</b>取的是筛选结果的前 N 条
    /// （<see cref="ExportOperationRecordsInputDto.MaximumExportCount"/> 封顶）。
    /// 全量导出需要异步生成与产物存储，本接口不提供。</para>
    /// </remarks>
    Task<OperationRecordExportFileDto> ExportAsync(
        ExportOperationRecordsInputDto input,
        CancellationToken cancellationToken = default);
}
