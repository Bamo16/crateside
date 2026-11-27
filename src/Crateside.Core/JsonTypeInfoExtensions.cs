using System.Text.Json;
using System.Text.Json.Nodes;
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

    /// <summary>Reads a file whose properties may still carry <paramref name="renamedKeys"/>' old names (old to new, any casing), so they load as the new.</summary>
    public static TValue? ReadFileOrDefault<TValue>(
        this JsonTypeInfo<TValue> jsonTypeInfo,
        string path,
        IReadOnlyDictionary<string, string> renamedKeys
    )
    {
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize(
                    RenameKeys(JsonNode.Parse(File.ReadAllText(path)), renamedKeys),
                    jsonTypeInfo
                )
                : default;
        }
        catch (Exception ex)
            when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return default;
        }
    }

    /// <summary>Renames every property at any depth whose name is an old one, unless its new name is already there.</summary>
    internal static JsonNode? RenameKeys(
        JsonNode? node,
        IReadOnlyDictionary<string, string> renamedKeys
    )
    {
        switch (node)
        {
            case JsonObject properties:
                foreach (var (name, child) in properties.ToList())
                {
                    RenameKeys(child, renamedKeys);

                    if (
                        renamedKeys.FirstOrDefault(pair =>
                            string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase)
                        )
                            is { Value: { } renamed }
                        && !properties.ContainsKey(renamed)
                    )
                    {
                        properties.Remove(name);
                        properties[renamed] = child;
                    }
                }

                break;
            case JsonArray items:
                foreach (var item in items)
                    RenameKeys(item, renamedKeys);

                break;
        }

        return node;
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
