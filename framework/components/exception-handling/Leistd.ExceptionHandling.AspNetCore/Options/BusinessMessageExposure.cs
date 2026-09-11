namespace Leistd.ExceptionHandling.AspNetCore.Options;

/// <summary>
/// 业务异常的 <c>Message</c> 在多大范围内可以呈现给终端用户。
/// </summary>
/// <remarks>
/// 这是宿主的一条<b>统一策略</b>，不是每个抛出点各自的决定：抛出点只负责把原因说清楚，
/// "这条能不能给用户看"由部署方按状态码类别一次定死。让每个调用点自己判断的做法试过，
/// 结果是同一种错误在不同入口表现不一致，而判据又只存在于写那行代码的人脑子里。
/// </remarks>
public enum BusinessMessageExposure
{
    /// <summary>
    /// 一律不呈现：只用词条，查不到就用状态短语。
    /// </summary>
    /// <remarks>对外网关这类"任何内部文字都不外露"的部署用它。</remarks>
    None,

    /// <summary>
    /// 只呈现客户端错误（4xx）的消息，服务端错误（5xx）不呈现。默认值。
    /// </summary>
    /// <remarks>
    /// 这条线才是真正的风险边界：4xx 说的是"你的输入哪里不对"，本来就得让调用方看见；
    /// 5xx 说的是"系统内部出了什么事"（连接串解析失败、上游超时），那是诊断信息，
    /// 只该进日志。<c>Message</c> 无论哪种情况都完整进日志。
    /// </remarks>
    ClientErrors,

    /// <summary>
    /// 一律呈现，包括 5xx。
    /// </summary>
    /// <remarks>仅内部系统适用：它会把运维诊断信息暴露给终端用户。</remarks>
    All,
}
