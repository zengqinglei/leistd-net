using CompanyName.ProjectName.Application.OperationRecords.AppServices;
using CompanyName.ProjectName.Application.OperationRecords.Dtos;
using CompanyName.ProjectName.Application.Permissions.Provider;
using Leistd.Ddd.Application.Contracts.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CompanyName.ProjectName.Api.Controllers;

/// <summary>
/// 操作记录：什么人在什么时间做了什么、结果如何。
/// </summary>
/// <remarks>
/// 只有查询。记录写下之后不再修改或删除，因此没有写端点——
/// 一张能被改写的审计表不能作为证据。
/// </remarks>
[Authorize]
[Route("api/v1/operation-records")]
public sealed class OperationRecordController(IOperationRecordAppService operationRecordAppService) : BaseController
{
    /// <summary>按创建时间倒序分页查询操作记录（需要操作记录查看权限）。</summary>
    [HttpGet]
    [Authorize(Policy = PermissionConstant.OperationRecords.Default)]
    public Task<PagedResultDto<OperationRecordOutputDto>> GetPagedListAsync(
        [FromQuery] GetOperationRecordPagedInputDto input,
        CancellationToken cancellationToken)
        => operationRecordAppService.GetPagedListAsync(input, cancellationToken);

    /// <summary>下发筛选项：有哪些类别、哪些动作码（需要操作记录查看权限）。</summary>
    /// <remarks>
    /// 界面不硬编码动作码——那等于把服务端的登记复制一份到前端，两处迟早漂移，
    /// 而症状是"某个动作在筛选框里选不到"，没有任何报错。
    /// </remarks>
    [HttpGet("filter-options")]
    [Authorize(Policy = PermissionConstant.OperationRecords.Default)]
    public Task<OperationRecordFilterOptionsOutputDto> GetFilterOptionsAsync(
        CancellationToken cancellationToken)
        => operationRecordAppService.GetFilterOptionsAsync(cancellationToken);

    /// <summary>把当前筛选条件下的记录导出为 CSV（需要操作记录导出权限）。</summary>
    /// <remarks>
    /// <para>导出与查看<b>分开授权</b>：整批带离系统的影响面与在线翻页不是一个量级。</para>
    /// <para><b>本项目不包响应信封</b>，控制器返回什么客户端就收到什么，因此文件流直接返回即可。
    /// 框架里确有 <c>Leistd.Response.AspNetCore</c> 这个可选组件（带 <c>[NoWrap]</c> 用于放行文件流），
    /// 但模板没有引用它、也没有调用 <c>AddResponseWrapper()</c>。
    /// <b>若将来启用了信封，这个端点必须同时加上放行特性</b>——否则文件被包一层就不再是文件，
    /// 浏览器下载下来打不开，且没有任何报错。</para>
    /// </remarks>
    [HttpGet("export")]
    [Authorize(Policy = PermissionConstant.OperationRecords.Export)]
    public async Task<IActionResult> ExportAsync(
        [FromQuery] ExportOperationRecordsInputDto input,
        CancellationToken cancellationToken)
    {
        var file = await operationRecordAppService.ExportAsync(input, cancellationToken);
        return File(file.Content, file.ContentType, file.FileName);
    }
}
