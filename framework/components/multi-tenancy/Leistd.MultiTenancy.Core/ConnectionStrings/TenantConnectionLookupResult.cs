namespace Leistd.MultiTenancy.ConnectionStrings;

/// <summary>
/// 按连接名查询租户连接的结果。
/// </summary>
/// <remarks>
/// <para>去掉模式标志位之后，"该租户不单独分库"与"该租户分了库但少登记了这个服务的连接"在单条记录上
/// 已经无法区分，而两者的正确处理截然相反：前者用宿主自己的配置，后者必须失败关闭。
/// <see cref="HasAnyConnection"/> 就是这条判据。</para>
/// <list type="bullet">
/// <item><see cref="HasAnyConnection"/> 为 <see langword="false"/>：租户一条连接都没登记，用宿主配置；</item>
/// <item><see cref="HasAnyConnection"/> 为 <see langword="true"/> 且 <see cref="Connection"/> 非空：用它；</item>
/// <item><see cref="HasAnyConnection"/> 为 <see langword="true"/> 但 <see cref="Connection"/> 为
/// <see langword="null"/>：租户是分库租户却缺这个名字、也没有默认名可回落，<b>拒绝</b>。</item>
/// </list>
/// <para><see cref="Connection"/> 已由实现应用过"精确名 → 默认名"的回落，调用方不需要自己再找一遍。</para>
/// </remarks>
public sealed class TenantConnectionLookupResult
{
    /// <summary>被查询的租户 Id。</summary>
    public required Guid TenantId { get; init; }

    /// <summary>该租户是否登记过<b>任意</b>连接。</summary>
    public required bool HasAnyConnection { get; init; }

    /// <summary>
    /// 命中的连接；<see langword="null"/> 表示这个名字（及默认名）都没有登记。
    /// </summary>
    public TenantConnectionConfiguration? Connection { get; init; }

    /// <inheritdoc />
    public override string ToString() =>
        $"{nameof(TenantConnectionLookupResult)} {{ TenantId = {TenantId}, " +
        $"HasAnyConnection = {HasAnyConnection}, Name = {Connection?.Name ?? "<none>"} }}";
}
