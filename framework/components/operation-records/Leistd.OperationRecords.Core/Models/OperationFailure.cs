namespace Leistd.OperationRecords.Models;

/// <summary>
/// 一次操作失败的原因：<b>为什么没成</b>。
/// </summary>
/// <remarks>
/// <para><b>失败原因分两类，区别对待。</b></para>
/// <para><b>其一，可枚举的业务规则拒绝</b>（"订单已发货，不能删除"）：存
/// <see cref="Code"/> + <see cref="Data"/>，展示期按当前语言渲染。这与动作码是同一套路子——
/// 存渲染好的句子会把语言永久锁死：写入时是哪国语言，此后所有读者看到的就是哪国语言，
/// 改不回来。Django 为此把"落库前关闭翻译、读取时再翻译"写进了源码注释；
/// Discourse 的中文译文把占位符顺序整个翻转，证明连"拼接片段"都不可行。</para>
/// <para><b>其二，不可枚举的技术异常</b>（远程接口超时、数据库报错）：走
/// <see cref="Detail"/>，不本地化。</para>
/// <para><b>安全边界：本类型刻意不提供接受任意 <see cref="Exception"/> 的工厂。</b>
/// 只接 <c>BusinessException</c>（其消息本就是给日志的英文诊断，错误码才是契约）；
/// 技术异常必须由调用方显式调用 <see cref="FromDetail"/>。原因是这张表在多租户下
/// <b>由租户管理员直接阅读</b>，而原始异常文本会带上表名列名、内部地址、主机名，
/// 乃至连接串。若提供了自动捕获的重载，<c>catch (Exception ex) { ...FromException(ex) }</c>
/// 会成为最顺手的写法，一次疏忽就是信息泄露。让危险的那条路必须手写，
/// 是为了逼调用方逐次决定"哪段文字可以给租户看"。</para>
/// </remarks>
public readonly record struct OperationFailure
{
    private OperationFailure(string? code, string? data, string? detail)
    {
        Code = code;
        Data = data;
        Detail = detail;
    }

    /// <summary>
    /// 失败原因的稳定错误码，兼本地化资源键（如 <c>Order:AlreadyShipped</c>）。
    /// </summary>
    public string? Code { get; }

    /// <summary>
    /// 本地化占位参数，序列化为 JSON 对象；无参数时为 <see langword="null"/>。
    /// </summary>
    /// <remarks>
    /// 它只服务于<b>失败原因</b>的文案模板。动作句子的模板占位符不因此扩张——
    /// 允许任意参数进入动作句子，正是"渲染后存句子"那条路的起点。
    /// </remarks>
    public string? Data { get; }

    /// <summary>
    /// 面向排查的技术说明，不本地化。
    /// </summary>
    /// <remarks>
    /// <b>仅宿主可见</b>（见 <see cref="OperationVisibility"/>）：它可能带表名、内部地址与主机名，
    /// 属于宿主的基础设施形态，租户管理员不该看到。展示上只进详情区，不进主列。
    /// </remarks>
    public string? Detail { get; }

    /// <summary>没有失败原因（成功路径，或原因不适用）。</summary>
    public static OperationFailure None { get; } = new(null, null, null);

    /// <summary>是否携带了任何原因信息。</summary>
    public bool IsEmpty => Code is null && Detail is null;

    /// <summary>
    /// 由错误码与可选的本地化参数构造。
    /// </summary>
    /// <param name="code">错误码兼资源键。为空白时退化为 <see cref="None"/>。</param>
    /// <param name="dataJson">
    /// 已序列化为 JSON 对象的占位参数。由调用方序列化而不是本类型代劳：
    /// 组件不该为了一个可选字段把 JSON 序列化器拖进依赖。
    /// </param>
    public static OperationFailure FromCode(string? code, string? dataJson = null)
        => string.IsNullOrWhiteSpace(code)
            ? None
            : new(code.Trim(), Normalize(dataJson), null);

    /// <summary>
    /// 由技术说明构造，用于不可枚举的异常。
    /// </summary>
    /// <remarks>
    /// <b>调用方对内容负责。</b>传进来的文字会被租户管理员看到（若记录本身对租户可见），
    /// 因此不要直接传 <c>exception.ToString()</c> 或原始异常消息，
    /// 而应传一句你确认可以公开的说明，例如"调用支付网关超时"。
    /// </remarks>
    /// <param name="detail">可公开的技术说明。</param>
    public static OperationFailure FromDetail(string? detail)
        => string.IsNullOrWhiteSpace(detail) ? None : new(null, null, detail.Trim());

    /// <summary>同时带错误码与技术说明。</summary>
    public static OperationFailure Create(string? code, string? dataJson, string? detail)
        => new(
            string.IsNullOrWhiteSpace(code) ? null : code.Trim(),
            Normalize(dataJson),
            string.IsNullOrWhiteSpace(detail) ? null : detail.Trim());

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
