using Microsoft.Extensions.Options;

namespace Crateside.Tests.Loudness;

/// <summary>Options read from the test on every use, as appsettings would be by the app.</summary>
internal sealed class CurrentOptions<T>(Func<T> current) : IOptionsMonitor<T>
{
    public T CurrentValue => current();

    public T Get(string? name) => current();

    public IDisposable? OnChange(Action<T, string?> listener) => null;
}
