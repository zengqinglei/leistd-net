using System.ComponentModel.DataAnnotations;
using Leistd.Ddd.Application.Contracts.Dtos;
using Leistd.OperationRecords.Abstractions;
using CompanyName.ProjectName.Application.OperationRecords.Provider;

namespace CompanyName.ProjectName.Application.OperationRecords.Dtos;

/// <summary>
/// 一条操作记录：什么人、在什么时间、做了什么、结果如何。
/// </summary>
/// <remarks>
/// 这里刻意重述框架的 <see cref="OperationRecordInfo"/> 而不是直接下发它：
/// API 契约与组件的内部形态是两件事，组件换字段不该直接改变对外契约。
/// </remarks>
public record OperationRecordOutputDto
{
    public required Guid Id { get; init; }

    /// <summary>业务动作码，界面按它本地化。</summary>
    public required string Action { get; init; }

    /// <summary>操作目标标识；没有目标时为 <c>-</c>。</summary>
    public required string TargetId { get; init; }

    /// <summary>操作目标的人类可读名快照；取不到时为空。</summary>
    /// <remarks>
    /// 授权阶段被拒的记录没有名字，这是<b>正确</b>的：那条路径上调用方正因无权访问该目标而被拒，
    /// 回填名字等于把他无权查看的内容写进了他能读到的记录里。界面应据此降级显示标识。
    /// </remarks>
    public string? TargetName { get; init; }

    /// <summary>授权依据：权限名，或业务自己的标记。</summary>
    public required string AuthorizationBasis { get; init; }

    /// <summary>结果：<c>Succeeded</c> 或 <c>Failed</c>。</summary>
    public required string Outcome { get; init; }

    public required DateTime CreationTime { get; init; }

    /// <summary>操作人标识；机器主体为空。</summary>
    public string? ActorId { get; init; }

    /// <summary>操作人显示名的快照——存的是当时的名字，不随后来改名而变。</summary>
    public string? ActorName { get; init; }

    /// <summary>
    /// 这条记录的主体是否就是 <see cref="TargetName"/> 本人；界面据此在操作人缺失时回落到目标名。
    /// </summary>
    /// <remarks>
    /// <para>登录这类<b>自证</b>动作发生在认证之前，请求主体当时确实是匿名的，
    /// <see cref="ActorId"/> 为空是诚实的记录，而不是缺陷（见 <c>AuthAppService</c> 里的说明）。
    /// 但界面把这种记录显示成"匿名"会让人读不懂——"什么人"其实由目标承载着。</para>
    /// <para><b>判定放在服务端而不是界面。</b>界面按动作码前缀去猜，等于把服务端的动作登记
    /// 复制一份到前端，两处迟早漂移；更要紧的是 <c>auth.login.failed</c> 的目标是调用方
    /// 提交的用户名、未经任何验证，放它回落就等于让任何人都能往审计界面的操作人列里写文本。
    /// 因此这里只对<b>成功</b>且<b>自证</b>的动作置真。</para>
    /// <para><b>不进 CSV 导出。</b>导出里 <c>ActorId/ActorName</c> 与 <c>TargetId/TargetName</c>
    /// 都是原样下发的，消费方看得见事实；这个字段是显示提示，把它写进文件等于让一个视图层的
    /// 判断变成对外契约的一部分。</para>
    /// </remarks>
    public bool ActorIsTarget { get; init; }

    /// <summary>
    /// 模拟登录时的<b>真实</b>操作人显示名；非模拟场景为空。
    /// </summary>
    /// <remarks>
    /// 有值即表示这次操作是宿主管理员以租户身份做的：<see cref="ActorName"/> 是被模拟的租户用户，
    /// 而真正按下按钮的是这里的人。界面要把两者一起显示，否则追责会指向一个什么都没做的人。
    /// </remarks>
    public string? ImpersonatorName { get; init; }

    /// <summary>失败原因的错误码，界面按它本地化；成功时为空。</summary>
    /// <remarks>
    /// 存码而不是渲染好的句子：写入时是哪国语言，此后所有读者看到的就是哪国语言，改不回来。
    /// </remarks>
    public string? FailureCode { get; init; }

    /// <summary>失败原因的本地化占位参数（JSON 对象）。</summary>
    public string? FailureData { get; init; }

    /// <summary>
    /// 面向排查的技术说明；<b>仅宿主可见</b>，租户读者拿到的恒为空。
    /// </summary>
    /// <remarks>
    /// 它可能带表名、内部地址与主机名，属于宿主的基础设施形态。裁剪在服务端完成，
    /// 不依赖界面"不显示"——前端不显示不等于没下发。
    /// </remarks>
    public string? FailureDetail { get; init; }

    /// <summary>
    /// 链路标识，用于按它去请求日志里查这次调用的完整细节；<b>仅宿主可见</b>。
    /// </summary>
    /// <remarks>对租户是无用且泄露内部拓扑的标识，故与 <see cref="FailureDetail"/> 同样裁剪。</remarks>
    public string? CorrelationId { get; init; }

    /// <summary>
    /// 操作发生时的租户；<b>仅宿主可见</b>，宿主上下文里的操作为空。
    /// </summary>
    /// <remarks>
    /// 租户用户调用宿主接口被拒时，记录写进宿主层，靠它回答"是哪个租户的人"。
    /// 对租户读者它恒为自己，没有信息量，故同样裁剪。
    /// </remarks>
    public Guid? ActorTenantId { get; init; }
}

/// <summary>
/// 筛选项：界面据此渲染类别与动作的下拉，不硬编码动作码。
/// </summary>
/// <remarks>
/// 按当前读者的可见性裁剪——租户读者拿不到 <c>Host</c> 层动作，
/// 否则筛选框里会摆着一堆他永远筛不出东西的项。
/// </remarks>
public record OperationRecordFilterOptionsOutputDto
{
    /// <summary>出现过的类别，按动作登记顺序去重。</summary>
    public required IReadOnlyList<string> Categories { get; init; }

    /// <summary>可选的动作，含各自所属类别与严重度。</summary>
    public required IReadOnlyList<OperationActionOptionDto> Actions { get; init; }
}

/// <summary>一个可选的操作动作。</summary>
public record OperationActionOptionDto
{
    /// <summary>动作码，界面按它查句子模板。</summary>
    public required string Code { get; init; }

    /// <summary>所属类别，用于按类别联动筛选。</summary>
    public required string Category { get; init; }

    /// <summary>严重度：<c>Info</c> / <c>Notice</c> / <c>Critical</c>。</summary>
    public required string Severity { get; init; }
}

/// <summary>
/// 操作记录导出入参。
/// </summary>
/// <remarks>
/// <b>刻意不继承 <see cref="PagedRequestDto"/>。</b>那个基类的 <c>MaximumLimit</c> 是
/// 面向翻页的影响面封顶（1000），它的注释也写明「确需更大值的接口应自定义入参类型」——
/// 导出正是这种接口。这里不带 <c>Offset</c>：导出取的是「筛选结果的前 N 条」，
/// 给一个偏移量只会让人以为能靠翻页拼出全量，而那需要的是异步导出子系统。
/// <para><b>上限 <see cref="MaximumExportCount"/> 条是同步导出的边界，不是全量。</b>
/// 按每行约 300 字节估算约 3MB，同步生成与传输都在合理区间；再往上响应时长与内存
/// 都不再适合跑在请求线程上。<b>"导出全部"本接口做不到</b>，这一点不含糊。</para>
/// </remarks>
public record ExportOperationRecordsInputDto : IValidatableObject
{
    /// <summary>单次导出的最大条数。</summary>
    public const int MaximumExportCount = 10000;

    /// <summary>关键字，同时匹配动作码、目标标识与操作人名。</summary>
    [Display(Name = "Search keyword")]
    [MaxLength(256, ErrorMessage = "{0} cannot exceed {1} characters.")]
    public string? Keyword { get; init; }

    /// <summary>起始时刻（含），UTC；为空则不设下界。</summary>
    [Display(Name = "Start time")]
    public DateTime? StartTime { get; init; }

    /// <summary>结束时刻（含），UTC；为空则不设上界。</summary>
    [Display(Name = "End time")]
    public DateTime? EndTime { get; init; }

    /// <summary>按类别筛选（多选，命中任一即匹配）；为空则不过滤。</summary>
    [Display(Name = "Categories")]
    [MaxLength(20, ErrorMessage = "{0} cannot contain more than {1} items.")]
    public List<string>? Categories { get; init; }

    /// <summary>按动作码筛选（多选，命中任一即匹配）；为空则不过滤。与类别同时给出时取交集。</summary>
    [Display(Name = "Actions")]
    [MaxLength(50, ErrorMessage = "{0} cannot contain more than {1} items.")]
    public List<string>? Actions { get; init; }

    /// <summary>按结果筛选；为空则不过滤。取值 <c>Succeeded</c> 或 <c>Failed</c>。</summary>
    [Display(Name = "Outcome")]
    [AllowedValues(
        null,
        nameof(OperationRecordOutcome.Succeeded),
        nameof(OperationRecordOutcome.Failed),
        ErrorMessage = "{0} is not an allowed value.")]
    public string? Outcome { get; init; }

    /// <summary>导出条数，取值 1–<see cref="MaximumExportCount"/>。</summary>
    [Display(Name = "Limit")]
    [Range(1, MaximumExportCount, ErrorMessage = "{0} must be between {1} and {2}.")]
    public int Limit { get; init; } = MaximumExportCount;

    /// <summary>
    /// 与分页查询同样的入口校验：起止倒置在这里拒掉，结果取值由属性上的 <c>[AllowedValues]</c> 限定。
    /// </summary>
    /// <remarks>
    /// 两处校验必须一致。导出这条路径放行了分页会拒的输入，就会出现
    /// 「界面筛不出来、导出却导得到」——那比没有导出更糟。
    /// </remarks>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (StartTime.HasValue && EndTime.HasValue && StartTime > EndTime)
        {
            yield return new ValidationResult(
                "The start time must not be later than the end time.",
                [nameof(StartTime), nameof(EndTime)]);
        }
    }
}

/// <summary>
/// 导出产物：内容、媒体类型与文件名。
/// </summary>
/// <remarks>
/// 应用服务返回字节而不是 <c>IActionResult</c>：应用层不该依赖 MVC 的返回类型，
/// 否则它就只能从控制器调用。控制器负责把它包成 <c>File(...)</c>。
/// </remarks>
public record OperationRecordExportFileDto
{
    /// <summary>文件内容（UTF-8，含 BOM）。</summary>
    public required byte[] Content { get; init; }

    /// <summary>媒体类型。</summary>
    public required string ContentType { get; init; }

    /// <summary>建议的下载文件名。</summary>
    public required string FileName { get; init; }
}

/// <summary>
/// 操作记录分页查询入参。
/// </summary>
/// <remarks>
/// 不提供排序参数：这张表只有一种有意义的读法——按时间倒序看最近发生了什么。
/// 给一个能改排序的入口，只会让人翻出一页"最早的几条"，而那没有任何用途。
/// </remarks>
public record GetOperationRecordPagedInputDto : PagedRequestDto, IValidatableObject
{
    /// <summary>关键字，同时匹配动作码、目标标识与操作人名。</summary>
    [Display(Name = "Search keyword")]
    [MaxLength(256, ErrorMessage = "{0} cannot exceed {1} characters.")]
    public string? Keyword { get; init; }

    /// <summary>起始时刻（含），UTC；为空则不设下界。</summary>
    [Display(Name = "Start time")]
    public DateTime? StartTime { get; init; }

    /// <summary>结束时刻（含），UTC；为空则不设上界。</summary>
    [Display(Name = "End time")]
    public DateTime? EndTime { get; init; }

    /// <summary>
    /// 按类别筛选（多选，命中任一即匹配）；为空则不过滤。
    /// </summary>
    /// <remarks>
    /// <b>类别在本层展开成动作码后再下传</b>：类别定义在 <c>IOperationActionDefinition</c> 上，
    /// 而记录表里只有动作码。让存储去查定义管理器，等于给它加一条它不该有的依赖——
    /// 那与可见范围"调用方算好、存储只照做"是同一条边界。
    /// </remarks>
    [Display(Name = "Categories")]
    [MaxLength(20, ErrorMessage = "{0} cannot contain more than {1} items.")]
    public List<string>? Categories { get; init; }

    /// <summary>按动作码筛选（多选，命中任一即匹配）；为空则不过滤。与类别同时给出时取交集。</summary>
    [Display(Name = "Actions")]
    [MaxLength(50, ErrorMessage = "{0} cannot contain more than {1} items.")]
    public List<string>? Actions { get; init; }

    /// <summary>按结果筛选；为空则不过滤。</summary>
    /// <remarks>
    /// 取值为 <c>Succeeded</c> 或 <c>Failed</c>。<b>保留独立筛选维度而不是写进动作码</b>：
    /// NIST AU-3 与 OWASP 都把 success/fail 列为审计记录的必需内容，混进动作码就没法独立筛选与告警。
    /// </remarks>
    [Display(Name = "Outcome")]
    [AllowedValues(
        null,
        nameof(OperationRecordOutcome.Succeeded),
        nameof(OperationRecordOutcome.Failed),
        ErrorMessage = "{0} is not an allowed value.")]
    public string? Outcome { get; init; }

    /// <summary>
    /// 起止倒置时在入口拒绝。
    /// </summary>
    /// <remarks>
    /// 不拦的话查询会正常执行并返回空列表，而那看起来就是"这段时间什么也没发生"——
    /// 对一张审计表来说，把"条件写反了"显示成"无人操作过"是最危险的一种误导。
    /// </remarks>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (StartTime.HasValue && EndTime.HasValue && StartTime > EndTime)
        {
            yield return new ValidationResult(
                "The start time must not be later than the end time.",
                [nameof(StartTime), nameof(EndTime)]);
        }
    }
}
