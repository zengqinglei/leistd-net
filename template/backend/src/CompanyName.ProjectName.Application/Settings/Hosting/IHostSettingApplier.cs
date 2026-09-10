namespace CompanyName.ProjectName.Application.Settings.Hosting;

/// <summary>
/// 把宿主级设置应用到进程内的可变状态上
/// </summary>
/// <remarks>
/// 宿主级设置（<c>SettingScopes.Host</c>）配的是进程级的东西——日志级别之类，
/// 消费方不是每次用的时候去查库，而是读一份进程内的可变状态。改完设置得有人把新值推过去，
/// 那就是这个接口。
/// <para>
/// 实现放在能碰到具体运行时的那一层（例如日志级别开关在 Api 层，Serilog 只在那里可见），
/// 应用层只声明契约，不知道被应用到了什么东西上。
/// </para>
/// <para>
/// 两处会调它：写入设置成功之后（本进程立即生效），以及后台的周期刷新
/// （其它实例跟上——设置行是共享的，而进程内状态不是）。
/// </para>
/// </remarks>
public interface IHostSettingApplier
{
    /// <summary>读取当前的宿主级设置并应用。</summary>
    /// <remarks>必须在宿主上下文调用：宿主级设置只有宿主那一行。</remarks>
    /// <param name="cancellationToken">取消令牌。</param>
    Task ApplyAsync(CancellationToken cancellationToken = default);
}
