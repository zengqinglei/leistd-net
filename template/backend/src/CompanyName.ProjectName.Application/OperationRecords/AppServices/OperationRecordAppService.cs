using CompanyName.ProjectName.Application.OperationRecords.Dtos;
using CompanyName.ProjectName.Application.Permissions.Provider;
using Leistd.Authorization.Abstractions;
using Leistd.Ddd.Application.AppService;
using Leistd.Ddd.Application.Contracts.Dtos;
using Leistd.ExceptionHandling;
using System.Globalization;
using System.Text;
using Leistd.MultiTenancy.Abstractions;
using Leistd.OperationRecords.Abstractions;
using Leistd.Security.Users;
using Leistd.Timing;
using CompanyName.ProjectName.Application.OperationRecords.Provider;
using CompanyName.ProjectName.Application.OperationRecords.Mappings;
using Leistd.ObjectMapping.Abstractions;

namespace CompanyName.ProjectName.Application.OperationRecords.AppServices;

/// <summary>
/// 操作记录查询应用服务实现。
/// </summary>
/// <remarks>
/// 租户隔离由组件实体的全局查询过滤器承担，本服务不带租户条件：宿主看宿主的、租户看自己的。
/// 权限侧别因此是 <c>Both</c>，不需要再按侧别分一次。
/// </remarks>
public class OperationRecordAppService(
    IOperationRecordStore operationRecordStore,
    IOperationActionDefinitionManager actionDefinitions,
    IPermissionChecker permissionChecker,
    ICurrentTenant currentTenant,
    ICurrentUser currentUser,
    IOperationRecorder operationRecorder,
    IObjectMapper objectMapper,
    IClock clock) : BaseAppService, IOperationRecordAppService
{
    /// <inheritdoc />
    public async Task<PagedResultDto<OperationRecordOutputDto>> GetPagedListAsync(
        GetOperationRecordPagedInputDto input,
        CancellationToken cancellationToken = default)
    {
        // 控制器上的策略已经拦过一道；这里再判一次是因为应用服务也可能被其它入口调用
        // （后台作业、内部服务），而审计内容本身就是敏感数据。
        if (!await permissionChecker.IsGrantedAsync(PermissionConstant.OperationRecords.Default, cancellationToken))
            throw new ForbiddenException("Viewing operation records requires the operation records permission.");

        // "谁是宿主"由本层判定后交给存储，而不是让组件自己去问上下文——
        // 存储的契约是"不做租户判定"，把这类判断挪进去会让它长出不该有的依赖。
        var (scope, isHostReader) = ResolveScope();

        // 时间区间原样下传：入参已声明为 UTC，这里再做一次时区换算只会把同一件事做两遍。
        //
        // **取消令牌必须具名传**：它前面是几个可选参数，按位置传时一旦中间插入新的可选参数，
        // 令牌就会静默落到那个参数位上。类型不兼容时是编译错，兼容时则是运行期的怪问题——
        // 两种都难排查，而具名传参让插入参数不再有副作用。
        // 类别在本层展开成动作码，再与显式给出的动作码取交集——两者是不同维度的筛选，维度之间是 AND。
        // 存储层不认识类别（它定义在 IOperationActionDefinition 上，记录里只有动作码）。
        var requestedActions = ResolveRequestedActions(input.Categories, input.Actions);

        // **"给了筛选条件但展开为空"必须返回空页，不能下传。**
        // 存储层把空集合当作"不过滤"（那是清空筛选的正常意图，有专门用例钉住）。
        // 但用户选了某个类别、而该类别下一个已登记动作都没有时，展开结果同样是空集合——
        // 此时若下传，就把"筛了但没有"显示成了"这就是全部"，与入口处拒绝倒置时间区间是同一种误导。
        if (requestedActions is { Count: 0 })
        {
            return new PagedResultDto<OperationRecordOutputDto>(0, []);
        }

        var outcome = ParseOutcome(input.Outcome);

        var page = await operationRecordStore.GetPagedListAsync(
            input.Keyword,
            input.StartTime,
            input.EndTime,
            input.Offset,
            input.Limit,
            scope,
            actions: requestedActions,
            outcome: outcome,
            cancellationToken: cancellationToken);

        return new PagedResultDto<OperationRecordOutputDto>(
            page.TotalCount,
            MapRecords(page.Items, isHostReader));
    }

    /// <inheritdoc />
    public async Task<OperationRecordFilterOptionsOutputDto> GetFilterOptionsAsync(
        CancellationToken cancellationToken = default)
    {
        if (!await permissionChecker.IsGrantedAsync(PermissionConstant.OperationRecords.Default, cancellationToken))
            throw new ForbiddenException("Viewing operation records requires the operation records permission.");

        // 筛选项按可见性裁剪：租户读者看不到 Host 层的记录，选项里就不该列出那些动作——
        // 否则筛选框里摆着一堆他永远筛不出东西的项，而"筛了没有"与"看不到"在界面上长得一样。
        var visible = VisibleDefinitions().ToList();

        return new OperationRecordFilterOptionsOutputDto
        {
            Categories = [.. visible.Select(definition => definition.Category).Distinct(StringComparer.Ordinal)],
            Actions =
            [
                .. visible.Select(definition => objectMapper.Map<IOperationActionDefinition, OperationActionOptionDto>(definition))
            ],
        };
    }

    /// <inheritdoc />
    public async Task<OperationRecordExportFileDto> ExportAsync(
        ExportOperationRecordsInputDto input,
        CancellationToken cancellationToken = default)
    {
        // 导出与查看分开授权：整批带离系统之后既不受可见性分层约束、也不再有访问记录。
        if (!await permissionChecker.IsGrantedAsync(PermissionConstant.OperationRecords.Export, cancellationToken))
            throw new ForbiddenException("Exporting operation records requires the operation records export permission.");

        var (scope, isHostReader) = ResolveScope();
        var requestedActions = ResolveRequestedActions(input.Categories, input.Actions);

        // "筛了但展开为空"要导出一份只有表头的文件，与分页那条路径返回空页同语义。
        // 若改为不下传筛选条件，导出的就是全量——用户以为自己导的是某个类别，
        // 拿到的却是所有记录，而文件本身看不出这个差别。
        IReadOnlyList<OperationRecordOutputDto> rows = [];
        if (requestedActions is not { Count: 0 })
        {
            var page = await operationRecordStore.GetPagedListAsync(
                input.Keyword,
                input.StartTime,
                input.EndTime,
                0,
                input.Limit,
                scope,
                actions: requestedActions,
                outcome: ParseOutcome(input.Outcome),
                cancellationToken: cancellationToken);

            // 与列表同一个映射，字段级裁剪自动继承：租户读者的技术详情与链路标识恒为空。
            rows = MapRecords(page.Items, isHostReader);
        }

        var content = BuildCsv(rows, isHostReader);

        // **导出成功之后才记**：记在前面，一旦后续抛异常就成了"记了但没发生"。
        await operationRecorder.RecordSucceededAsync(
            OperationRecordActions.OperationRecordsExported,
            OperationTarget.None,
            PermissionConstant.OperationRecords.Export,
            cancellationToken);

        return new OperationRecordExportFileDto
        {
            Content = content,
            ContentType = "text/csv",
            FileName = $"operation-records-{clock.Now:yyyyMMddHHmmss}.csv",
        };
    }

    /// <summary>把记录渲染成 CSV 字节。</summary>
    /// <remarks>
    /// <para><b>带 UTF-8 BOM。</b>不带的话 Excel 会按本地代码页解释，中文列全是乱码——
    /// 而用户不会认为这是 Excel 的问题，只会认为导出坏了。</para>
    /// <para><b>动作码存原样，不渲染成句子。</b>句子是视图：渲染后落进文件，
    /// 这份文件的语言就永久锁死了，与存储层不存句子是同一条约束。</para>
    /// <para>宿主专属两列<b>只在宿主导出时出现</b>。租户的文件里连列都没有，
    /// 而不是有列但为空——后者会让人以为是数据缺失，进而来问"为什么详情是空的"。</para>
    /// </remarks>
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
            }

            builder.AppendLine(string.Join(',', cells.Select(cell => EscapeCsv(cell ?? string.Empty))));
        }

        // Encoding.UTF8 的 GetPreamble 就是 BOM；显式拼在前面而不是靠 encoding 参数，
        // 是因为 GetBytes 本身从不产出 BOM，只有写流时才会——这里返回的是字节数组。
        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(builder.ToString())];
    }

    /// <summary>转义一个 CSV 字段。</summary>
    /// <remarks>
    /// <b>两件事，不能只做第一件：</b>
    /// <para>其一是 CSV 本身的转义——含逗号、引号或换行时整个字段加引号、内部引号翻倍。</para>
    /// <para>其二是<b>公式注入</b>：以 <c>= + - @</c> 开头的字段会被 Excel／WPS 当公式执行，
    /// 而目标名与操作人名是<b>用户可控内容</b>——攻击者只要把自己的显示名改成
    /// <c>=cmd|...</c>，就能让打开这份审计文件的管理员执行任意命令。前缀一个单引号即可，
    /// 它在表格里不显示，但让整格退化成文本。
    /// <c>\t</c> 与 <c>\r</c> 同样列入：它们会被吃掉，让后面的 <c>=</c> 变成首字符。</para>
    /// </remarks>
    private static string EscapeCsv(string value)
    {
        var neutralized = value.Length > 0 && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r'
            ? "'" + value
            : value;

        return neutralized.Contains(',') || neutralized.Contains('"') || neutralized.Contains('\n') || neutralized.Contains('\r')
            ? $"\"{neutralized.Replace("\"", "\"\"")}\""
            : neutralized;
    }

    /// <summary>当前读者的可见范围，以及他是不是宿主。</summary>
    /// <remarks>
    /// <b>抽出来是为了让分页与导出共用同一份判定。</b>两条路径各写一份的话迟早漂移，
    /// 而症状是「界面看不到的记录能被导出来」——那是越权，不是显示差异。
    /// </remarks>
    private (OperationRecordVisibilityScope Scope, bool IsHostReader) ResolveScope()
    {
        var isHostReader = currentTenant.Id is null;
        return (
            isHostReader
                ? OperationRecordVisibilityScope.Host
                : OperationRecordVisibilityScope.ForTenantReader(currentUser.Id?.ToString()),
            isHostReader);
    }

    /// <summary>解析结果筛选值；非法或为空都返回 <see langword="null"/>（不过滤）。</summary>
    /// <remarks>
    /// 必须与两个入参 DTO 的 <c>Validate</c> 用同一个 <c>ignoreCase</c>：不一致会让校验放行的值
    /// 在这里解析失败、静默退化成"不过滤"——而那正是 <c>Validate</c> 要拦的那种误导。
    /// </remarks>
    private static OperationRecordOutcome? ParseOutcome(string? outcome) =>
        Enum.TryParse<OperationRecordOutcome>(outcome, ignoreCase: true, out var parsed)
            ? parsed
            : null;

    /// <summary>把类别展开成动作码，并与显式给出的动作码取交集；都未给出时返回 <see langword="null"/>。</summary>
    /// <remarks>
    /// <para>类别与动作是两个筛选维度：维度内多选取并集，维度之间取交集。界面上动作候选随类别联动，
    /// 就是在类别之内再收窄；若两者取并集，选了类别之后再选动作不会少掉任何一条——看起来筛了，其实没有。</para>
    /// 返回 <see langword="null"/> 与返回空集合是<b>两种不同的意图</b>：前者是"没按动作筛"，
    /// 后者是"筛了但一个都不匹配"。调用处据此决定是下传还是直接返回空页。
    /// </remarks>
    private List<string>? ResolveRequestedActions(List<string>? inputCategories, List<string>? inputActions)
    {
        var hasCategories = inputCategories is { Count: > 0 };
        var hasActions = inputActions is { Count: > 0 };
        if (!hasCategories && !hasActions)
        {
            return null;
        }

        HashSet<string>? codes = null;

        if (hasCategories)
        {
            var categories = new HashSet<string>(inputCategories!, StringComparer.Ordinal);
            codes = VisibleDefinitions()
                .Where(d => categories.Contains(d.Category))
                .Select(d => d.Code)
                .ToHashSet(StringComparer.Ordinal);
        }

        if (hasActions)
        {
            // 显式给出的动作码同样过一遍可见性：否则租户读者可以通过直接传码
            // 来试探宿主侧动作是否存在（查不到记录，但选项是否合法本身也是信息）。
            var visibleCodes = VisibleDefinitions().Select(d => d.Code).ToHashSet(StringComparer.Ordinal);
            var requested = inputActions!.Where(visibleCodes.Contains).ToHashSet(StringComparer.Ordinal);
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

    /// <summary>当前读者可见的动作定义。</summary>
    private IEnumerable<IOperationActionDefinition> VisibleDefinitions()
    {
        var isHostReader = currentTenant.Id is null;
        return actionDefinitions.GetAll()
            .Where(definition => isHostReader || definition.Visibility != OperationVisibility.Host);
    }

    // 宿主读者才看得到技术详情与链路标识，裁剪在映射里（见 OperationRecordProfile）
    private List<OperationRecordOutputDto> MapRecords(IReadOnlyList<OperationRecordInfo> records, bool isHostReader)
    {
        var context = new Dictionary<string, object> { [OperationRecordProfile.IncludeHostOnlyFieldsKey] = isHostReader };
        return [.. records.Select(record => objectMapper.Map<OperationRecordInfo, OperationRecordOutputDto>(record, context))];
    }
}
