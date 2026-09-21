using Leistd.Notifications.Dtos;
using Leistd.Notifications.Filters;
using Leistd.Notifications.Settings.Options;
using Leistd.Settings.Definitions;
using Leistd.Settings.Errors;
using Leistd.Settings.Management;
using Leistd.Settings.Resolution;
using Leistd.Settings.Stores;
using Microsoft.Extensions.Options;

namespace Leistd.Notifications.Settings.Filters;

// 读收件人自己的生效值（不是当前请求者的）：发布方往往是别人或后台任务。
// 没有定义对应设置的组合、或值解析不了一律投递——新加一个类别不会因为忘了定义偏好而收不到。
internal sealed class SettingsNotificationDeliveryFilter(
    ISettingProvider settingProvider,
    ISettingDefinitionManager definitionManager,
    IOptions<NotificationPreferenceOptions> options) : INotificationDeliveryFilter
{
    public async Task<bool> ShouldDeliverAsync(
        string userId,
        NotificationOutputDto notification,
        string channel,
        CancellationToken ct = default)
    {
        var preferences = options.Value;
        if (preferences.MandatoryDeliveries.Contains(new NotificationDelivery(notification.Type, channel)))
        {
            return true;
        }

        var definition = definitionManager.GetOrNull(preferences.SettingNameOf(notification.Type, channel));
        if (definition is null)
        {
            return true;
        }

        var value = await settingProvider.GetOrNullForUserAsync(definition.Name, userId, ct);
        return !bool.TryParse(value, out var allowed) || allowed;
    }
}
