using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Crateside.Core;

internal static class JsonTypeInfoExtensions
{
    /// <summary>Null when the file is missing, unreadable or malformed.</summary>
    public static TValue? ReadFileOrDefault<TValue>(
        this JsonTypeInfo<TValue> jsonTypeInfo,
        string path
    )
    {
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize(File.ReadAllText(path), jsonTypeInfo)
                : default;
        }
        catch (Exception ex)
            when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return default;
        }
    }

    /// <summary>Writes through a temporary file, so a failed write never leaves a torn one.</summary>
    public static void WriteFile<TValue>(
        this JsonTypeInfo<TValue> jsonTypeInfo,
        string path,
        TValue value
    )
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = $"{path}.tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(value, jsonTypeInfo));
        File.Move(temporary, path, overwrite: true);
    }
}
