using System.Globalization;
using System.Text;
using Leistd.Data.Paging;
using Leistd.ExceptionHandling;
using Leistd.MultiTenancy.Abstractions;
using Leistd.OperationRecords.Abstractions;
using Leistd.OperationRecords.Dtos;
using Leistd.OperationRecords.Options;
using Leistd.Security.Users;
using Leistd.Timing;
using Microsoft.Extensions.Options;

namespace Leistd.OperationRecords.Services;

// 查询、筛选项与导出共用同一份读者判定与字段裁剪：两条路径各写一份迟早漂移，
// 症状是"界面看不到的记录能被导出来"——那是越权，不是显示差异。
internal sealed class OperationRecordQueryService(
    IOperationRecordStore store,
    IOperationActionDefinitionManager actionDefinitions,
    IOperationRecorder recorder,
    ICurrentTenant currentTenant,
    ICurrentUser currentUser,
    IClock clock,
    IOptions<OperationRecordOptions> options) : IOperationRecordQueryService
{
    public async Task<PagedResult<OperationRecordOutputDto>> GetPagedListAsync(
        GetOperationRecordPagedInputDto input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        UnprocessableEntityException.ThrowIfInvalid(input);

        var reader = ResolveReader();

        // 给了筛选条件但展开为空必须返回空页：存储把空集合当"不过滤"，
        // 下传就把"筛了但没有"显示成了"这就是全部"
        var actions = ResolveRequestedActions(input.Categories, input.Actions, reader.IsHost);
        if (actions is { Count: 0 })
        {
            return PagedResult<OperationRecordOutputDto>.Empty;
        }

        var page = await store.GetPagedListAsync(
            Filter(reader, input.Keyword, input.StartTime, input.EndTime, actions, input.Outcome),
            input,
            cancellationToken);

        return new PagedResult<OperationRecordOutputDto>(page.TotalCount, [.. page.Items.Select(record => Map(record, reader.IsHost))]);
    }

    public Task<OperationRecordFilterOptionsOutputDto> GetFilterOptionsAsync(CancellationToken cancellationToken = default)
    {
        // 筛选项按可见性裁剪：否则筛选框里摆着一堆读者永远筛不出东西的项，
        // 而"筛了没有"与"看不到"在界面上长得一样
        var visible = VisibleDefinitions(ResolveReader().IsHost).ToList();

        return Task.FromResult(new OperationRecordFilterOptionsOutputDto
        {
            Categories = [.. visible.Select(definition => definition.Category).Distinct(StringComparer.Ordinal)],
            Actions =
            [
                .. visible.Select(definition => new OperationActionOptionDto
                {
                    Code = definition.Code,
                    Category = definition.Category,
                    Severity = definition.Severity.ToString()
                })
            ]
        });
    }

    public async Task<OperationRecordExportFileDto> ExportAsync(
        ExportOperationRecordsInputDto input,
        OperationRecordExportAudit audit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(audit);
        UnprocessableEntityException.ThrowIfInvalid(input);

        var reader = ResolveReader();
        var actions = ResolveRequestedActions(input.Categories, input.Actions, reader.IsHost);

        // "筛了但展开为空"导出只有表头的文件，与分页返回空页同语义；不下传筛选条件就成了导出全量
        IReadOnlyList<OperationRecordOutputDto> rows = [];
        if (actions is not { Count: 0 })
        {
            var page = await store.GetPagedListAsync(
                Filter(reader, input.Keyword, input.StartTime, input.EndTime, actions, input.Outcome),
                new PageRequest { Limit = input.Limit },
                cancellationToken);
            rows = [.. page.Items.Select(record => Map(record, reader.IsHost))];
        }

        var content = BuildCsv(rows, reader.IsHost);

        // 文件生成之后才记：记在前面，一旦后续抛异常就成了"记了但没发生"
        await recorder.RecordSucceededAsync(audit.Action, OperationTarget.None, audit.AuthorizationBasis, cancellationToken);

        return new OperationRecordExportFileDto(
            content,
            "text/csv",
            $"operation-records-{clock.Now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture)}.csv");
    }

    private (OperationRecordVisibilityScope Scope, bool IsHost) ResolveReader()
    {
        if (currentTenant.Id is null)
        {
            return (OperationRecordVisibilityScope.Host, true);
        }

        // 读者标识与记录器取操作人的口径一致（claim 原始值）：只认 ICurrentUser.Id 的话，
        // 机器主体读不到自己写下的 Actor 层记录
        var actorId = currentUser.FindClaim(options.Value.ActorIdClaimType)?.Value ?? currentUser.Id?.ToString();
        return (OperationRecordVisibilityScope.ForTenantReader(actorId), false);
    }

    private static OperationRecordFilter Filter(
        (OperationRecordVisibilityScope Scope, bool IsHost) reader,
        string? keyword,
        DateTime? startTime,
        DateTime? endTime,
        IReadOnlyCollection<string>? actions,
        string? outcome) => new()
        {
            Scope = reader.Scope,
            Keyword = keyword,
            StartTime = startTime,
            EndTime = endTime,
            Actions = actions,
            // 取值已由入参上的 [AllowedValues] 限定为成员名称；不用 Enum.TryParse 兜底，它会把数字当合法值
            Outcome = outcome is null ? null : Enum.Parse<OperationRecordOutcome>(outcome)
        };

    // 类别与动作是两个维度：维度内并集、维度间交集。返回 null 表示"没按动作筛"，
    // 空集合表示"筛了但一个都不匹配"——调用处据此决定是下传还是直接返回空页。
    // 显式给出的动作码同样过一遍可见性，否则租户读者能通过直接传码试探宿主侧动作是否存在。
    private List<string>? ResolveRequestedActions(
        IReadOnlyList<string>? categories,
        IReadOnlyList<string>? requestedActions,
        bool isHost)
    {
        var hasCategories = categories is { Count: > 0 };
        var hasActions = requestedActions is { Count: > 0 };
        if (!hasCategories && !hasActions)
        {
            return null;
        }

        var visible = VisibleDefinitions(isHost).ToList();
        HashSet<string>? codes = null;

        if (hasCategories)
        {
            var categorySet = new HashSet<string>(categories!, StringComparer.Ordinal);
            codes = visible.Where(d => categorySet.Contains(d.Category)).Select(d => d.Code).ToHashSet(StringComparer.Ordinal);
        }

        if (hasActions)
        {
            var visibleCodes = visible.Select(d => d.Code).ToHashSet(StringComparer.Ordinal);
            var requested = requestedActions!.Where(visibleCodes.Contains).ToHashSet(StringComparer.Ordinal);
            if (codes is null)
            {
                codes = requested;
            }
            else
            {
                codes.IntersectWith(requested);
            }
        }

        return [.. codes!];
    }

    private IEnumerable<IOperationActionDefinition> VisibleDefinitions(bool isHost)
        => actionDefinitions.GetAll().Where(definition => isHost || definition.Visibility != OperationVisibility.Host);

    private OperationRecordOutputDto Map(OperationRecordInfo record, bool isHost) => new()
    {
        Id = record.Id,
        Action = record.Action,
        TargetId = record.TargetId,
        TargetName = record.TargetName,
        AuthorizationBasis = record.AuthorizationBasis,
        Outcome = record.Outcome.ToString(),
        CreationTime = record.CreationTime,
        ActorId = record.ActorId,
        ActorName = record.ActorName,
        // 自证类动作成功时主体刚刚被证实、请求里还没有操作人，目标承载的就是"什么人"
        ActorIsTarget = record.ActorId is null
                        && record.Outcome == OperationRecordOutcome.Succeeded
                        && actionDefinitions.GetOrNull(record.Action)?.TargetIsActor == true,
        ImpersonatorName = record.ImpersonatorName,
        FailureCode = record.FailureCode,
        FailureData = record.FailureData,
        FailureDetail = isHost ? record.FailureDetail : null,
        CorrelationId = isHost ? record.CorrelationId : null,
        ActorTenantId = isHost ? record.ActorTenantId : null
    };

    // CSV：带 UTF-8 BOM（否则 Excel 按本地代码页解释，中文全是乱码）；动作码存原样不渲染句子
    // （句子落进文件，这份文件的语言就锁死了）；仅宿主那几列只在宿主导出时成列，而不是有列但为空。
    private static byte[] BuildCsv(IReadOnlyList<OperationRecordOutputDto> rows, bool includeHostOnlyColumns)
    {
        var builder = new StringBuilder();

        List<string> header =
        [
            "CreationTime(UTC)", "Action", "Outcome", "ActorId", "ActorName",
            "ImpersonatorName", "TargetId", "TargetName", "AuthorizationBasis",
            "FailureCode", "FailureData"
        ];
        if (includeHostOnlyColumns)
        {
            header.Add("FailureDetail");
            header.Add("CorrelationId");
            header.Add("ActorTenantId");
        }

        builder.AppendLine(string.Join(',', header.Select(EscapeCsv)));

        foreach (var row in rows)
        {
            List<string?> cells =
            [
                row.CreationTime.ToString("o", CultureInfo.InvariantCulture),
                row.Action, row.Outcome, row.ActorId, row.ActorName,
                row.ImpersonatorName, row.TargetId, row.TargetName, row.AuthorizationBasis,
                row.FailureCode, row.FailureData
            ];
            if (includeHostOnlyColumns)
            {
                cells.Add(row.FailureDetail);
                cells.Add(row.CorrelationId);
                cells.Add(row.ActorTenantId?.ToString());
            }

            builder.AppendLine(string.Join(',', cells.Select(cell => EscapeCsv(cell ?? string.Empty))));
        }

        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(builder.ToString())];
    }

    // 两件事：CSV 转义，以及公式注入——以 = + - @ 开头的字段会被 Excel／WPS 当公式执行，
    // 而目标名与操作人名是用户可控内容。前缀单引号让整格退化成文本；\t 与 \r 会被吃掉，同样列入。
    private static string EscapeCsv(string value)
    {
        var neutralized = value.Length > 0 && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r'
            ? "'" + value
            : value;

        return neutralized.Contains(',') || neutralized.Contains('"') || neutralized.Contains('\n') || neutralized.Contains('\r')
            ? $"\"{neutralized.Replace("\"", "\"\"")}\""
            : neutralized;
    }

}
