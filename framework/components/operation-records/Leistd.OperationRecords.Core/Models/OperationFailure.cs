using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;

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
/// <para><b>安全边界：本类型不提供接受 <see cref="Exception"/> 的工厂</b>，
/// 包括 <c>BusinessException</c> 在内。原因是这张表在多租户下<b>由租户管理员直接阅读</b>，
/// 而原始异常文本会带上表名列名、内部地址、主机名，乃至连接串。若提供了自动捕获的重载，
/// <c>catch (Exception ex) { ...FromException(ex) }</c> 会成为最顺手的写法，一次疏忽就是信息泄露。
/// 让危险的那条路必须手写，是为了逼调用方逐次决定"哪段文字可以给租户看"。</para>
/// <para>业务异常的码与参数请显式传入，两个组件经 BCL 类型对接，互不引用：
/// <code>OperationFailure.FromCode(exception.Code, exception.LocalizationData)</code>
/// 其中的字典要先剔除不该给租户看的键——哪些键敏感只有应用自己知道。</para>
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
    /// 本地化占位参数，序列化为扁平 JSON 对象；无参数时为 <see langword="null"/>。
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
    /// <param name="data">
    /// 本地化占位参数，键与词条里的占位名一一对应。值必须是标量
    /// （字符串、布尔、数值、日期、<see cref="Guid"/> 或自报格式的类型）；
    /// 其余类型只写类型名、不写内容——理由见 <see cref="OperationFailure"/>。
    /// </param>
    public static OperationFailure FromCode(string? code, IReadOnlyDictionary<string, object?>? data = null)
        => string.IsNullOrWhiteSpace(code)
            ? None
            : new(code.Trim(), SerializeData(data), null);

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
    /// <inheritdoc cref="FromCode" path="/param[@name='data']"/>
    public static OperationFailure Create(string? code, IReadOnlyDictionary<string, object?>? data, string? detail)
        => new(
            string.IsNullOrWhiteSpace(code) ? null : code.Trim(),
            SerializeData(data),
            string.IsNullOrWhiteSpace(detail) ? null : detail.Trim());

    // 不用 JsonSerializer.Serialize(data)：值是 object?，反射序列化会把传进来的复杂对象整个摊开，
    // 而这一列租户管理员直接可读、还会进导出。这里只写标量，其余只写类型名——
    // 既让能落进去的东西有界，也与消费端一致：词条占位符 {{X}} 渲染不了嵌套对象。
    private static string? SerializeData(IReadOnlyDictionary<string, object?>? data)
    {
        if (data is null || data.Count == 0)
        {
            return null;
        }

        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            foreach (var (name, value) in data)
            {
                writer.WritePropertyName(name);
                WriteScalar(writer, value);
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static void WriteScalar(Utf8JsonWriter writer, object? value)
    {
        switch (value)
        {
            case null:
                writer.WriteNullValue();
                break;
            case string text:
                writer.WriteStringValue(text);
                break;
            case bool flag:
                writer.WriteBooleanValue(flag);
                break;
            case byte or sbyte or short or ushort or int or uint or long:
                writer.WriteNumberValue(Convert.ToInt64(value, CultureInfo.InvariantCulture));
                break;
            case ulong number:
                writer.WriteNumberValue(number);
                break;
            // NaN 与正负无穷不是合法的 JSON 数值，Utf8JsonWriter 会直接抛异常——
            // 记录失败原因这一步自己失败，会把本该返回的业务错误变成 500。按字面量写成字符串
            case float or double when !double.IsFinite(Convert.ToDouble(value, CultureInfo.InvariantCulture)):
                writer.WriteStringValue(((IFormattable)value).ToString(null, CultureInfo.InvariantCulture));
                break;
            case float or double:
                writer.WriteNumberValue(Convert.ToDouble(value, CultureInfo.InvariantCulture));
                break;
            case decimal number:
                writer.WriteNumberValue(number);
                break;
            case DateTime moment:
                writer.WriteStringValue(moment);
                break;
            case DateTimeOffset moment:
                writer.WriteStringValue(moment);
                break;
            case Guid id:
                writer.WriteStringValue(id);
                break;
            // 自报格式的类型（枚举、TimeSpan、自定义数值……）按区域无关字面量写出
            case IFormattable formattable:
                writer.WriteStringValue(formattable.ToString(null, CultureInfo.InvariantCulture));
                break;
            // 其余一律只写类型名，**不写内容**。ToString() 在这里是泄露面：匿名类型与 record
            // 会把每个成员的值原样吐出来，而这一列租户管理员直接可读、还会进导出。
            // 何况词条占位符 {{X}} 渲染不了对象，传对象进来本身就是调用方的编码错误——
            // 记录里出现 [<>f__AnonymousType0`2] 是让它显形，比悄悄带出连接串好。
            default:
                writer.WriteStringValue($"[{value.GetType().Name}]");
                break;
        }
    }
}
