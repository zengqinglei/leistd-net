using Leistd.Data.Paging;
using Leistd.OperationRecords.Abstractions;
using Leistd.OperationRecords.Dtos;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Leistd.OperationRecords.AspNetCore.Endpoints;

/// <summary>
/// 操作记录的 HTTP 端点。
/// </summary>
public static class OperationRecordEndpoints
{
    /// <summary>端点名前缀，宿主按名字给个别端点追加约定时使用。</summary>
    public const string NamePrefix = "Leistd.OperationRecords.";

    /// <summary>分页查询端点名。</summary>
    public const string GetPagedListName = NamePrefix + "GetPagedList";

    /// <summary>筛选项端点名。</summary>
    public const string GetFilterOptionsName = NamePrefix + "GetFilterOptions";

    /// <summary>导出端点名。</summary>
    public const string ExportName = NamePrefix + "Export";

    /// <summary>
    /// 映射操作记录的查询、筛选项与导出端点：<c>GET /</c>、<c>GET /filter-options</c>、<c>GET /export</c>。
    /// </summary>
    /// <remarks>
    /// <para>前缀由宿主的路由组决定；返回的路由组可继续追加约定（限流、OpenAPI 标签、响应包装）。
    /// 授权策略全部必填，漏配在映射时抛出。</para>
    /// <para>查询参数与控制器形态一致：<c>offset</c>、<c>limit</c>、<c>keyword</c>、<c>startTime</c>、<c>endTime</c>、
    /// 可重复的 <c>categories</c> 与 <c>actions</c>、<c>outcome</c>；入参校验失败返回带字段错误的 422。</para>
    /// <para>不要改写成 <c>[AsParameters]</c> 绑定分页类型：它会把没有默认值的非空属性当成必填参数。</para>
    /// </remarks>
    /// <example>
    /// <code>
    /// app.MapGroup("/api/v1/operation-records").MapOperationRecords(options =&gt;
    /// {
    ///     options.ReadPolicy = "App.OperationRecords";
    ///     options.ExportPolicy = "App.OperationRecords.Export";
    ///     options.ExportAction = "operation-records.exported";
    /// });
    /// </code>
    /// </example>
    /// <param name="endpoints">路由构建器（通常是宿主的路由组）。</param>
    /// <param name="configure">授权口径。</param>
    /// <returns>承载这组端点的路由组。</returns>
    public static RouteGroupBuilder MapOperationRecords(
        this IEndpointRouteBuilder endpoints,
        Action<OperationRecordEndpointOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new OperationRecordEndpointOptions();
        configure(options);
        options.Validate();

        var group = endpoints.MapGroup(string.Empty);

        group.MapGet(string.Empty, (
                IOperationRecordQueryService service,
                CancellationToken cancellationToken,
                int offset = 0,
                int limit = PageRequest.DefaultLimit,
                string? keyword = null,
                DateTime? startTime = null,
                DateTime? endTime = null,
                string[]? categories = null,
                string[]? actions = null,
                string? outcome = null) => service.GetPagedListAsync(
                new GetOperationRecordPagedInputDto
                {
                    Offset = offset,
                    Limit = limit,
                    Keyword = keyword,
                    StartTime = startTime,
                    EndTime = endTime,
                    Categories = categories,
                    Actions = actions,
                    Outcome = outcome
                },
                cancellationToken))
            .WithName(GetPagedListName)
            .RequireAuthorization(options.ReadPolicy);

        group.MapGet("filter-options", (IOperationRecordQueryService service, CancellationToken cancellationToken)
                => service.GetFilterOptionsAsync(cancellationToken))
            .WithName(GetFilterOptionsName)
            .RequireAuthorization(options.ReadPolicy);

        var audit = new OperationRecordExportAudit(options.ExportAction, options.ExportPolicy);
        group.MapGet("export", async (
                IOperationRecordQueryService service,
                CancellationToken cancellationToken,
                string? keyword = null,
                DateTime? startTime = null,
                DateTime? endTime = null,
                string[]? categories = null,
                string[]? actions = null,
                string? outcome = null,
                int limit = ExportOperationRecordsInputDto.MaximumExportCount) =>
            {
                var file = await service.ExportAsync(
                    new ExportOperationRecordsInputDto
                    {
                        Keyword = keyword,
                        StartTime = startTime,
                        EndTime = endTime,
                        Categories = categories,
                        Actions = actions,
                        Outcome = outcome,
                        Limit = limit
                    },
                    audit,
                    cancellationToken);
                return TypedResults.File(file.Content, file.ContentType, file.FileName);
            })
            .WithName(ExportName)
            .RequireAuthorization(options.ExportPolicy);

        return group;
    }
}
