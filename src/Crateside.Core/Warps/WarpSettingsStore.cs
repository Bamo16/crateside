using System.Text.Json.Serialization;

namespace Crateside.Core.Warps;

/// <summary>
/// The auto-warp setting the user chose in Settings, kept in <c>%APPDATA%\crateside\warp-settings.json</c>
/// so it outlives a restart and wins over appsettings.
/// </summary>
public sealed class WarpSettingsStore(CratesidePaths paths)
{
    private readonly string _path = paths.WarpSettingsFile;

    private readonly Lock _gate = new();

    internal WarpSettingsStore(string path)
        : this(new CratesidePaths { WarpSettingsFile = path }) { }

    /// <summary>Never throws: an unreadable file reads as nothing chosen.</summary>
    public AutoWarps? Read()
    {
        lock (_gate)
            return
                WarpSettingsJsonContext.Default.WarpSettings.ReadFileOrDefault(
                    _path,
                    LegacyWarpNames.StoredKeys
                )
                    is { AutoWarps: { } chosen }
                ? chosen
                : null;
    }

    /// <summary>Never throws: an unreadable file reads as nothing chosen.</summary>
    public bool? ReadFlagClipOffBarLine()
    {
        lock (_gate)
            return WarpSettingsJsonContext
                .Default.WarpSettings.ReadFileOrDefault(_path, LegacyWarpNames.StoredKeys)
                ?.FlagClipOffBarLine;
    }

    public void Remember(AutoWarps chosen) =>
        Write(settings => settings with { AutoWarps = chosen });

    public void RememberFlagClipOffBarLine(bool flag) =>
        Write(settings => settings with { FlagClipOffBarLine = flag });

    private void Write(Func<WarpSettings, WarpSettings> change)
    {
        lock (_gate)
        {
            try
            {
                var settings =
                    WarpSettingsJsonContext.Default.WarpSettings.ReadFileOrDefault(
                        _path,
                        LegacyWarpNames.StoredKeys
                    ) ?? new WarpSettings();

                WarpSettingsJsonContext.Default.WarpSettings.WriteFile(_path, change(settings));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}

public sealed record WarpSettings
{
    public AutoWarps? AutoWarps { get; init; }

    public bool? FlagClipOffBarLine { get; init; }
}

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNameCaseInsensitive = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    Converters = [typeof(JsonStringEnumConverter<AutoWarps>)],
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
)]
[JsonSerializable(typeof(WarpSettings))]
internal sealed partial class WarpSettingsJsonContext : JsonSerializerContext { }
