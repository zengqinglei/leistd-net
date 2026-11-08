using System.Reflection;
using Leistd.EventBus.Distributed.Events;
using Leistd.EventBus.Distributed.Options;
using Leistd.EventBus.Events;
using Microsoft.Extensions.Options;

namespace Leistd.EventBus.Distributed.Subscriptions;

internal sealed class DistributedEventSubscriptions(
    IEnumerable<DistributedEventSubscription> registrations,
    IOptions<DistributedEventOptions> options) : IDistributedEventSubscriptions
{
    private readonly DistributedEventSubscription[] subscriptions = Validate(registrations);

    public IReadOnlyList<DistributedEventSubscription> GetAll()
        => options.Value.TestEvent.Enabled
            ? subscriptions
            : subscriptions.Where(x => x.EventType != typeof(DistributedEventTestEvent)).ToArray();

    private static DistributedEventSubscription[] Validate(IEnumerable<DistributedEventSubscription> registrations)
    {
        var items = registrations.Distinct().ToArray();
        foreach (var group in items.GroupBy(x => x.EventName))
        {
            if (group.Select(x => x.EventType).Distinct().Count() > 1)
            {
                throw new InvalidOperationException($"Distributed event name '{group.Key}' is mapped to multiple CLR types.");
            }
        }

        foreach (var group in items.GroupBy(x => (x.EventName, x.Consumer)))
        {
            if (group.Count() > 1)
            {
                throw new InvalidOperationException($"Distributed event '{group.Key.EventName}' consumer '{group.Key.Consumer}' is registered more than once.");
            }
        }
        return items;
    }
}

internal static class DistributedEventNames
{
    public static string Get(Type type)
        => type.GetCustomAttribute<EventNameAttribute>()?.Name
           ?? type.FullName
           ?? throw new ArgumentException("A distributed event type must have a stable full name.", nameof(type));
}
