namespace Leistd.MultiTenancy.Abstractions;

/// <summary>
/// 多租户组件抛出的错误码，默认译文随包分发，宿主资源里的同名词条优先。
/// </summary>
public static class MultiTenancyErrorCodes
{
    /// <summary>租户不存在或已删除（404）。</summary>
    public const string NotFound = "Tenant:NotFound";

    /// <summary>租户已停用（403）。</summary>
    public const string NotActive = "Tenant:NotActive";

    /// <summary>租户名已被占用（409）。占位：<c>Name</c>。</summary>
    public const string DuplicateName = "Tenant:DuplicateName";

    /// <summary>租户被并发修改（409）。</summary>
    public const string ConcurrencyConflict = "Tenant:ConcurrencyConflict";

    /// <summary>主体带了多条租户声明（400）。</summary>
    public const string AmbiguousTenantClaim = "Tenant:AmbiguousClaim";

    /// <summary>启用前置条件不满足（400），由 <c>ITenantActivationGuard</c> 的实现给出更具体的码时以它为准。</summary>
    public const string ActivationRejected = "Tenant:ActivationRejected";

    /// <summary>专属库不可达（400）。</summary>
    public const string DedicatedDatabaseUnreachable = "Tenant:DedicatedDatabaseUnreachable";

    /// <summary>专属库不存在（400）。</summary>
    public const string DedicatedDatabaseMissing = "Tenant:DedicatedDatabaseMissing";

    /// <summary>专属库未迁移（400）。</summary>
    public const string DedicatedDatabaseNotMigrated = "Tenant:DedicatedDatabaseNotMigrated";

    /// <summary>专属库拒绝了连接串里的凭据（400）。</summary>
    public const string DedicatedDatabaseRejected = "Tenant:DedicatedDatabaseRejected";


    /// <summary>连接名不合法（400）。占位：<c>Name</c>、<c>Pattern</c>。</summary>
    public const string ConnectionNameInvalid = "TenantConnection:NameInvalid";

    /// <summary>创建租户时同一个连接名给了多条（400）。占位：<c>Name</c>。</summary>
    public const string ConnectionNameDuplicated = "TenantConnection:NameDuplicated";

    /// <summary>连接串为空、超长或不是键值对语法（400）；消息不回显连接串。</summary>
    public const string ConnectionStringInvalid = "TenantConnection:ConnectionStringInvalid";

    /// <summary>连接登记的版本不符（409）。</summary>
    public const string ConnectionVersionConflict = "TenantConnection:VersionConflict";

    /// <summary>改变数据落点的连接变更要求先停用租户（409）。</summary>
    public const string ConnectionChangeRequiresInactiveTenant = "TenantConnection:ChangeRequiresInactiveTenant";
}
