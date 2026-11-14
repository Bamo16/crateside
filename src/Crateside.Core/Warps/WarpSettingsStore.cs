using System.Text.Json.Serialization;

namespace Crateside.Core.Warps;

/// <summary>
/// The starter-warp setting the user chose in Settings, kept in <c>%APPDATA%\crateside\warp-settings.json</c>
/// so it outlives a restart and wins over appsettings.
/// </summary>
public sealed class WarpSettingsStore(CratesidePaths paths)
{
    private readonly string _path = paths.WarpSettingsFile;

    private readonly Lock _gate = new();

    internal WarpSettingsStore(string path)
        : this(new CratesidePaths { WarpSettingsFile = path }) { }

    /// <summary>Never throws: an unreadable file reads as nothing chosen.</summary>
    public StarterWarps? Read()
    {
        lock (_gate)
            return
                WarpSettingsJsonContext.Default.WarpSettings.ReadFileOrDefault(_path)
                    is { StarterWarps: { } chosen }
                ? chosen
                : null;
    }

    /// <summary>Never throws: an unreadable file reads as nothing chosen.</summary>
    public bool? ReadFlagClipOffBarLine()
    {
        lock (_gate)
            return WarpSettingsJsonContext
                .Default.WarpSettings.ReadFileOrDefault(_path)
                ?.FlagClipOffBarLine;
    }

    public void Remember(StarterWarps chosen) =>
        Write(settings => settings with { StarterWarps = chosen });

    public void RememberFlagClipOffBarLine(bool flag) =>
        Write(settings => settings with { FlagClipOffBarLine = flag });

    private void Write(Func<WarpSettings, WarpSettings> change)
    {
        lock (_gate)
        {
            try
            {
                var settings =
                    WarpSettingsJsonContext.Default.WarpSettings.ReadFileOrDefault(_path)
                    ?? new WarpSettings();

                WarpSettingsJsonContext.Default.WarpSettings.WriteFile(_path, change(settings));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}

public sealed record WarpSettings
{
    public StarterWarps? StarterWarps { get; init; }

    public bool? FlagClipOffBarLine { get; init; }
}

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNameCaseInsensitive = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    Converters = [typeof(JsonStringEnumConverter<StarterWarps>)],
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
)]
[JsonSerializable(typeof(WarpSettings))]
internal sealed partial class WarpSettingsJsonContext : JsonSerializerContext { }
