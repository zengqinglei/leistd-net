using Leistd.Localization.Core.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Leistd.Localization.AspNetCore;

/// <summary>
/// 启动时预热各支持语言的资源缓存：把首个请求才会触发的解析/合并提前到启动阶段，
/// 让坏资源、culture 声明不一致等问题在启动日志里就暴露，而非在生产首个请求时才隐性发生（P4）。
/// </summary>
/// <remarks>
/// 复用 <see cref="RequestLocalizationOptions.SupportedUICultures"/> 作为预热清单，避免与 supportedCultures 配置重复。
/// 读取器自身对坏文件容错（跳过 + 告警），故此处即便某语言资源有问题也不会让启动失败。
/// </remarks>
internal sealed class LocalizationPreloadHostedService(
    JsonLocalizationResourceReader reader,
    IOptions<RequestLocalizationOptions> requestLocalizationOptions,
    ILogger<LocalizationPreloadHostedService> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var cultures = requestLocalizationOptions.Value.SupportedUICultures;
        if (cultures is null)
            return Task.CompletedTask;

        foreach (var culture in cultures)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            var count = reader.GetTexts(culture.Name).Count;
            logger.LogInformation("本地化预热：culture '{Culture}' 加载 {Count} 条文案。", culture.Name, count);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
