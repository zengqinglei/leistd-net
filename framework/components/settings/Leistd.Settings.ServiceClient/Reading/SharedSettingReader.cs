using System.ComponentModel;
using System.Globalization;
using Leistd.MultiTenancy.Context;
using Leistd.Settings.Dtos;
using Microsoft.Extensions.DependencyInjection;

namespace Leistd.Settings.ServiceClient.Reading;

// Scoped：当前租户取自调用方作用域（ICurrentTenantAccessor 可由宿主替换）；缓存与回源是进程级单例
internal sealed class SharedSettingReader(
    IEnumerable<SharedSettingProviderRegistration> registrations,
    IServiceProvider services,
    SharedSettingCache cache) : ISharedSettingReader
{
    private readonly Dictionary<string, SharedSettingProviderRegistration> _providers =
        registrations.ToDictionary(registration => registration.Name, StringComparer.Ordinal);

    public async Task<SharedSettingValue> GetAsync(string provider, string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var registration = _providers.GetValueOrDefault(provider)
                           ?? throw new InvalidOperationException(
                               $"Shared settings provider '{provider}' is not registered. Register it with AddSharedSettingsClient.");
        if (!registration.Fallbacks.TryGetValue(name, out var fallback))
        {
            throw new InvalidOperationException(
                $"Shared setting '{name}' is not declared for provider '{provider}'. Declare it, with its fallback, in AddSharedSettingsClient.");
        }

        // 多租户可选：没有注册时只有宿主那一份
        var tenantSegment = services.GetService<ICurrentTenant>()?.Id?.ToString() ?? SharedSettingsDto.HostSegment;
        var values = await cache.GetAsync(registration, tenantSegment, cancellationToken);
        return values is not null && values.Values.TryGetValue(name, out var value)
            ? new SharedSettingValue(value, values.Stale ? SharedSettingSource.Stale : SharedSettingSource.Current)
            : new SharedSettingValue(fallback, SharedSettingSource.Fallback);
    }

    public async Task<T?> GetAsync<T>(string provider, string name, CancellationToken cancellationToken = default)
    {
        var value = (await GetAsync(provider, name, cancellationToken)).Value;
        return string.IsNullOrEmpty(value) ? default : Convert<T>(value);
    }

    // 与 ISettingProvider.GetAsync<T> 同一转换：枚举忽略大小写，其余经 TypeDescriptor、固定不变文化
    private static T Convert<T>(string value)
    {
        var target = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
        if (target.IsEnum)
        {
            return (T)Enum.Parse(target, value, ignoreCase: true);
        }

        return (T)TypeDescriptor.GetConverter(target).ConvertFromString(null, CultureInfo.InvariantCulture, value)!;
    }
}
