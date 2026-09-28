using Leistd.Security.Claims;

namespace Leistd.OperationRecords.Options;

/// <summary>
/// 操作记录配置，经 <c>AddOperationRecords(options =&gt; ...)</c> 设置，不绑定配置节。
/// </summary>
/// <remarks>
/// 模拟登录的 claim 类型由宿主注入而非写死在组件里：签发主体的是宿主，它用什么名字只有它知道。
/// 默认值指向 <see cref="CustomClaimTypes"/>。操作人标识按 <c>ClaimTypeOptions.UserIds</c> 读取（<c>ICurrentUser.SubjectId</c>），不在这里另立一份。
/// </remarks>
public class OperationRecordOptions
{
    /// <summary>
    /// 真实操作人标识的 claim 类型；模拟登录时据它还原"谁在操作"。
    /// </summary>
    public string ImpersonatorIdClaimType { get; set; } = CustomClaimTypes.ImpersonatorUserId;

    /// <summary>
    /// 真实操作人显示名的 claim 类型。
    /// </summary>
    public string ImpersonatorNameClaimType { get; set; } = CustomClaimTypes.ImpersonatorUserName;
}
