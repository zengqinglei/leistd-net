namespace CompanyName.ProjectName.Api.Hosting;

/// <summary>OTLP 发送门槛：宿主启动完成后打开，此前发送层一律丢弃。</summary>
/// <remarks>
/// 不依赖任何服务：导出器的 HttpClient 在构建日志工厂时就会创建，那时解析 <see cref="IHostApplicationLifetime"/>
/// 会经日志依赖重入，得到第二个生命周期实例，SIGTERM 停掉的那个不是 <c>app.Run()</c> 等待的那个，进程不退出。
/// 由托管服务在宿主启动时把打开动作挂到 <see cref="IHostApplicationLifetime.ApplicationStarted"/> 上。
/// </remarks>
internal sealed class TelemetryExportGate
{
    private volatile bool isOpen;

    /// <summary>宿主是否已启动完成。</summary>
    public bool IsOpen => isOpen;

    /// <summary>打开门槛。</summary>
    public void Open() => isOpen = true;
}
