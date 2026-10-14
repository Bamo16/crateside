using System.Text.Json.Serialization;

namespace Crateside.Core.Warps;

/// <summary>
/// The starter-warp setting the user chose in Settings, kept in <c>%APPDATA%\crateside\warp-settings.json</c>
/// so it outlives a restart and wins over appsettings.
/// </summary>
public sealed class WarpSettingsStore(string? path = null)
{
    private readonly string _path =
        path
        ?? Environment.SpecialFolder.ApplicationData.GetFolderPath(
            "crateside",
            "warp-settings.json"
        );

    private readonly Lock _gate = new();

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

    public void Remember(StarterWarps chosen)
    {
        lock (_gate)
        {
            try
            {
                WarpSettingsJsonContext.Default.WarpSettings.WriteFile(
                    _path,
                    new WarpSettings { StarterWarps = chosen }
                );
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}

public sealed record WarpSettings
{
    public StarterWarps? StarterWarps { get; init; }
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
