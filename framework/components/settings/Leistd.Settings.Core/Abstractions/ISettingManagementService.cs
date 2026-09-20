using Leistd.Settings.Dtos;

namespace Leistd.Settings.Abstractions;

/// <summary>
/// 设置页的读写用例：按层级读取覆盖值，写当前用户的偏好或当前租户（宿主上下文下为宿主）的值。
/// </summary>
/// <remarks>
/// <para>只处理标记为 <see cref="ISettingDefinition.IsVisibleToClients"/> 的设置：不可见的设置多是运维阈值，
/// 经普通接口可读可写等于把内部参数暴露成了业务能力。</para>
/// <para>不做权限检查——谁能写租户值由端点的授权策略决定。层级越权（租户上下文写进程级设置）在这里拒绝。</para>
/// <para>业务代码消费设置值时注入 <see cref="ISettingProvider"/>，不经本用例。</para>
/// </remarks>
public interface ISettingManagementService
{
    /// <summary>获取当前上下文可见设置在各层级上的覆盖值。</summary>
    /// <remarks>
    /// 租户上下文不含进程级设置：下发一个只能看、改了还会被拒的项，比看不到更让人困惑。
    /// 机密设置不下发任何值，只报"已设置"。
    /// </remarks>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<IReadOnlyList<SettingOutputDto>> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>写入当前用户自己的偏好；值为 <see langword="null"/> 时恢复为上一层。</summary>
    /// <param name="input">要写入的设置。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <exception cref="ExceptionHandling.ForbiddenException">当前身份不是用户。</exception>
    /// <exception cref="ExceptionHandling.NotFoundException">设置不存在或不对客户端开放。</exception>
    Task SetForCurrentUserAsync(SetSettingInputDto input, CancellationToken cancellationToken = default);

    /// <summary>
    /// 写入当前租户的默认值；进程级设置写入宿主那一份，只允许在宿主上下文。
    /// </summary>
    /// <param name="input">要写入的设置。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <exception cref="ExceptionHandling.ForbiddenException">在租户上下文写进程级设置。</exception>
    /// <exception cref="ExceptionHandling.NotFoundException">设置不存在或不对客户端开放。</exception>
    Task SetForCurrentTenantAsync(SetSettingInputDto input, CancellationToken cancellationToken = default);
}
