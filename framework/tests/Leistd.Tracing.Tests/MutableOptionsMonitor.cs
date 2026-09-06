using Microsoft.Extensions.Options;

namespace Leistd.Tracing.Tests;

internal sealed class MutableOptionsMonitor<TOptions>(TOptions currentValue) : IOptionsMonitor<TOptions>
{
    public TOptions CurrentValue { get; private set; } = currentValue;

    public TOptions Get(string? name) => CurrentValue;

    public IDisposable? OnChange(Action<TOptions, string?> listener) => null;

    public void Set(TOptions value) => CurrentValue = value;
}
