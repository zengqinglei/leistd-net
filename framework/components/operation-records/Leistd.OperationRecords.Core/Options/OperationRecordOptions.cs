using Leistd.Security.Claims;

namespace Leistd.OperationRecords.Options;

/// <summary>
/// 操作记录配置。配置节 <c>Leistd:OperationRecords</c>。
/// </summary>
/// <remarks>
/// claim 类型由宿主注入而非写死在组件里：签发主体的是宿主，它用什么名字只有它知道。
/// 默认值指向 <see cref="CustomClaimTypes"/>，与 <c>MultiTenancyOptions.TenantClaimType</c> 同型——
/// 全框架的自定义 claim 名在那里统一归口，组件不各立一份。
/// </remarks>
public class OperationRecordOptions
{
    /// <summary>
    /// 操作人标识的 claim 类型。
    /// </summary>
    /// <remarks>
    /// 读的是<b>原始值</b>而不是 <c>ICurrentUser.Id</c>：后者只在能解析成 GUID 时有值，
    /// 而机器主体（<c>client:&lt;client_id&gt;</c>）与后台作业主体都不是 GUID——
    /// 只认 <c>Id</c> 会让这类操作记成无主的。
    /// </remarks>
    public string ActorIdClaimType { get; set; } = CustomClaimTypes.Subject;

    /// <summary>
    /// 真实操作人标识的 claim 类型；模拟登录时据它还原"谁在操作"。
    /// </summary>
    public string ImpersonatorIdClaimType { get; set; } = CustomClaimTypes.ImpersonatorUserId;

    /// <summary>
    /// 真实操作人显示名的 claim 类型。
    /// </summary>
    public string ImpersonatorNameClaimType { get; set; } = CustomClaimTypes.ImpersonatorUserName;
}
