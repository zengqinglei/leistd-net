using CompanyName.ProjectName.Application.OperationRecords.Dtos;
#if (LocalIdentity)
using CompanyName.ProjectName.Application.OperationRecords.Provider;
#endif
using Leistd.ObjectMapping.Mapster;
using Leistd.ObjectMapping.Mapster.Mapping;
using Leistd.OperationRecords.Abstractions;
using Mapster;

namespace CompanyName.ProjectName.Application.OperationRecords.Mappings;

/// <summary>
/// 操作记录映射配置
/// </summary>
public class OperationRecordProfile : MapsterProfile
{
    /// <summary>
    /// MapContext 参数名：读者是否为宿主。
    /// </summary>
    /// <remarks>
    /// 不是宿主（或没给）时 <see cref="OperationRecordOutputDto.FailureDetail"/> 与
    /// <see cref="OperationRecordOutputDto.CorrelationId"/> 一律置空——<b>字段级裁剪必须在服务端做</b>，
    /// 交给界面"不显示"只是把数据下发了却假装看不见。列表与导出走同一个映射，裁剪口径自然一致。
    /// </remarks>
    public const string IncludeHostOnlyFieldsKey = "IncludeHostOnlyFields";

    protected override void ConfigureMappings()
    {
        CreateMap<OperationRecordInfo, OperationRecordOutputDto>()
            .Map(dest => dest.Outcome, src => src.Outcome.ToString())
            .Map(dest => dest.FailureDetail, src => IncludeHostOnlyFields() ? src.FailureDetail : null)
            .Map(dest => dest.CorrelationId, src => IncludeHostOnlyFields() ? src.CorrelationId : null)
            .Map(dest => dest.ActorIsTarget, src =>
                src.ActorId == null && src.Outcome == OperationRecordOutcome.Succeeded && IsSelfActing(src.Action));

        CreateMap<IOperationActionDefinition, OperationActionOptionDto>()
            .Map(dest => dest.Severity, src => src.Severity.ToString());
    }

    private static bool IncludeHostOnlyFields() =>
        MapContext.Current?.Parameters.TryGetValue(IncludeHostOnlyFieldsKey, out var value) == true && value is true;

    /// <summary>
    /// 这个动作的主体是否就是它的目标本人。
    /// </summary>
    /// <remarks>
    /// <para>只收<b>自证类</b>动作：主体在动作完成的那一刻才被证实，因此目标承载的就是"什么人"。</para>
    /// <para><b><c>auth.login.failed</c> 刻意不在其中。</b>它的目标是调用方提交的用户名、
    /// 未经验证；收了它，审计界面的操作人列就成了一个无需凭据即可写入任意文本的面。</para>
    /// <para>动作码引用常量而不是写字面量：这两份值必须与写入侧逐字一致，
    /// 抄一遍就等于埋下一处会静默漂移的重复。</para>
    /// <para><b>方法体必须按 <c>LocalIdentity</c> 分流。</b>这三个常量在
    /// <c>OperationRecordActions</c> 里只声明在 <c>LocalIdentity</c> 条件块内，
    /// 而本文件不带任何守卫、<c>Application/OperationRecords/**</c> 也不被 template.json 裁剪——
    /// 也就是说本文件在<b>每一种</b>形态下都参与编译，直接引用就会让 Resource 形态
    /// 报 <c>CS0117</c>。语义上也本该如此：资源服务没有本地登录，不存在自证类记录。</para>
    /// <para><b>这段说明刻意不写出条件指令的字面形式。</b>模板引擎按文本扫描指令，
    /// 不区分它在不在注释里：注释里出现字面指令会被当成真实指令，使配对错位、整段代码被静默吞掉，
    /// 生成时只表现为 <c>NullReferenceException</c> 或"某段代码莫名其妙没生成"。
    /// 「模板条件符号」闸门专为拦这个而设，别为了"写清楚一点"把它加回来。</para>
    /// </remarks>
    private static bool IsSelfActing(string action)
    {
#if (LocalIdentity)
        return action is OperationRecordActions.AuthLoginSucceeded
            or OperationRecordActions.AuthPasswordChanged
            or OperationRecordActions.AuthRegistered;
#else
        // 资源形态下没有可判定的自证动作，参数在这一支用不上——显式丢弃，
        // 免得"未使用参数"的提示被当成这里漏写了逻辑。
        _ = action;
        return false;
#endif
    }
}
