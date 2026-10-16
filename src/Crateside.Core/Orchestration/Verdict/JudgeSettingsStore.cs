using System.Text.Json.Serialization;

namespace Crateside.Core.Orchestration.Verdict;

/// <summary>The judging choices made in Settings, which override configuration (docs/judge.md).</summary>
public sealed class JudgeSettingsStore(string? path = null)
{
    private readonly string _path =
        path
        ?? Environment.SpecialFolder.ApplicationData.GetFolderPath(
            "crateside",
            "judge-settings.json"
        );

    private readonly Lock _gate = new();

    /// <summary>Never throws: an unreadable file reads as nothing chosen.</summary>
    public EarWeight? ReadEar()
    {
        lock (_gate)
            return JudgeSettingsJsonContext.Default.JudgeSettings.ReadFileOrDefault(_path)?.Ear;
    }

    public void RememberEar(EarWeight ear)
    {
        lock (_gate)
        {
            try
            {
                JudgeSettingsJsonContext.Default.JudgeSettings.WriteFile(
                    _path,
                    new JudgeSettings { Ear = ear }
                );
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}

public sealed record JudgeSettings
{
    public EarWeight? Ear { get; init; }
}

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNameCaseInsensitive = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    Converters = [typeof(JsonStringEnumConverter<EarWeight>)],
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
)]
[JsonSerializable(typeof(JudgeSettings))]
internal sealed partial class JudgeSettingsJsonContext : JsonSerializerContext { }
