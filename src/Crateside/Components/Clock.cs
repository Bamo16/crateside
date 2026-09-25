namespace Crateside.Components;

public static class Clock
{
    /// <summary>How long since <paramref name="since"/>, as m:ss or h:mm:ss.</summary>
    public static string Elapsed(DateTimeOffset since)
    {
        var span = DateTimeOffset.UtcNow - since;

        return span.TotalHours >= 1
            ? $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}"
            : $"{(int)span.TotalMinutes}:{span.Seconds:00}";
    }
}
