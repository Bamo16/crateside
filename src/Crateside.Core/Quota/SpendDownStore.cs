using System.Text.Json.Serialization;

namespace Crateside.Core.Quota;

/// <summary>When the weekly spend-down opens and where it stops (ADR 0063).</summary>
public sealed record SpendDownSettings
{
    /// <summary>How many hours before the weekly reset the spend-down opens.</summary>
    public int WindowHours { get; init; } = 3;

    /// <summary>Weekly use, as a percentage, at which the spend-down stops.</summary>
    public int WeeklyCeiling { get; init; } = 100;

    /// <summary>Five-hour use, as a percentage, at which the spend-down waits for the window to reset.</summary>
    public int SessionCeiling { get; init; } = 100;
}

/// <summary>The spend-down the user chose in Settings, kept in <c>%APPDATA%\crateside\spend-down.json</c>.</summary>
public sealed class SpendDownStore(CratesidePaths paths)
{
    private const int HoursInAWeek = 168;

    private readonly string _path = paths.SpendDownFile;

    private readonly Lock _gate = new();

    internal SpendDownStore(string path)
        : this(new CratesidePaths { SpendDownFile = path }) { }

    /// <summary>Never throws: an unreadable file reads as the defaults.</summary>
    public SpendDownSettings Read()
    {
        lock (_gate)
            return SpendDownJsonContext.Default.SpendDownSettings.ReadFileOrDefault(_path)
                ?? new SpendDownSettings();
    }

    public void Remember(SpendDownSettings chosen)
    {
        lock (_gate)
        {
            try
            {
                SpendDownJsonContext.Default.SpendDownSettings.WriteFile(
                    _path,
                    new SpendDownSettings
                    {
                        WindowHours = Math.Clamp(chosen.WindowHours, 1, HoursInAWeek),
                        WeeklyCeiling = Math.Clamp(chosen.WeeklyCeiling, 1, 100),
                        SessionCeiling = Math.Clamp(chosen.SessionCeiling, 1, 100),
                    }
                );
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase
)]
[JsonSerializable(typeof(SpendDownSettings))]
internal sealed partial class SpendDownJsonContext : JsonSerializerContext { }
