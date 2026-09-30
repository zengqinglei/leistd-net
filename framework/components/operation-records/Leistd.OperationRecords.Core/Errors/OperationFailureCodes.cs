namespace Leistd.OperationRecords.Errors;

/// <summary>
/// 本组件自身会写进 <see cref="Models.OperationFailure.Code"/> 的失败码。
/// </summary>
/// <remarks>
/// <para>只有这一个：除它之外，记录里出现的码全部来自宿主自己的业务拒绝。</para>
/// <para><b>这些码没有随包译文</b>，与其他组件的 <c>*ErrorCodes</c> 不同。
/// 失败原因按设计存码不存句子，查询时才按<b>当前读者</b>的语言渲染（理由见
/// <see cref="Models.OperationFailure"/>），译文由宿主资源提供，与宿主业务错误码同处。
/// 查不到词条时 <see cref="Dtos.OperationRecordOutputDto.FailureMessage"/> 为空、界面回落裸码，
/// 不会报错——这也意味着漏配是静默的，接入时请确认本类中的每个码在宿主资源里都有对应项。</para>
/// </remarks>
public static class OperationFailureCodes
{
    /// <summary>
    /// 授权未通过。由 <c>RecordDeniedOperationAsync</c> 在调用方未给出更具体的原因时补上。
    /// </summary>
    /// <remarks>无占位参数。</remarks>
    public const string Forbidden = "Error:Forbidden";
}
