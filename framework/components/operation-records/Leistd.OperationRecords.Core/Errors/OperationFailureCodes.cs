namespace Leistd.OperationRecords.Errors;

/// <summary>
/// 本组件自身会写进 <see cref="Models.OperationFailure.Code"/> 的失败码。
/// </summary>
/// <remarks>
/// <para>只有这一个：除它之外，记录里出现的码全部来自宿主自己的业务拒绝。</para>
/// <para>这些码是写进记录的<b>失败原因码</b>，不是协议层错误响应里的码：错误响应不会带它们。
/// 失败原因按设计存码不存句子，查询时才按<b>当前读者</b>的语言渲染（理由见 <see cref="Models.OperationFailure"/>）。
/// 中英默认译文随包分发，由 <c>AddOperationRecords()</c> 登记；宿主要改文案时在自己的资源里写同名键。</para>
/// </remarks>
public static class OperationFailureCodes
{
    /// <summary>
    /// 授权未通过。由 <c>RecordDeniedOperationAsync</c> 在调用方未给出更具体的原因时补上。
    /// </summary>
    /// <remarks>无占位参数。</remarks>
    public const string Forbidden = "Error:Forbidden";
}
