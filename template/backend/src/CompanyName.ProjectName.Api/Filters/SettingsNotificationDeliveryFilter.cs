#if (LocalIdentity)
#if (IncludeNotifications)
using CompanyName.ProjectName.Application.Notifications;
using CompanyName.ProjectName.Application.Settings.Provider;
using Leistd.Notifications.Filters;
using Leistd.Notifications.Dtos;
using Leistd.Settings.Abstractions;
using Leistd.Settings.Definitions;

namespace CompanyName.ProjectName.Api.Filters;

/// <summary>
/// 按收件人的通知偏好（用户设置 <c>Notifications.{类别}.{渠道}</c>）决定投不投。
/// </summary>
/// <remarks>
/// <para>直接按收件人读用户层的值，而不是经 <c>ISettingProvider</c>：后者解析的是<b>当前</b>用户，
/// 而通知常常发给别人，或发生在匿名请求里（登录时的新设备提醒）。</para>
/// <para>没有对应偏好设置的"类别 × 渠道"一律投递；安全提醒的站内通知不可关闭。</para>
/// </remarks>
public sealed class SettingsNotificationDeliveryFilter(
    ISettingStore settingStore,
    ISettingDefinitionManager definitionManager) : INotificationDeliveryFilter
{
    /// <inheritdoc />
    public async Task<bool> ShouldDeliverAsync(
        string userId,
        NotificationOutputDto notification,
        string channel,
        CancellationToken ct = default)
    {
        if (notification.Type == AppNotificationTypes.Security && channel == AppNotificationChannels.InApp)
            return true;

        var definition = definitionManager.GetOrNull(SettingConstant.Notifications.NameOf(notification.Type, channel));
        if (definition is null)
            return true;

        var userValues = await settingStore.GetAllAsync(SettingScopes.User, userId, ct);
        var value = userValues.GetValueOrDefault(definition.Name) ?? definition.DefaultValue;
        return !bool.TryParse(value, out var allowed) || allowed;
    }
}
#endif
#endif
