using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Crateside.Core.Ledger;

/// <summary>That a run's detector readings were written into its evidence after the verdict, so the judge never saw them (ADR 0066).</summary>
public sealed record LateReading(DateTimeOffset? At, string? Sha256)
{
    internal const string Field = "read_after_verdict";

    private static readonly JsonSerializerOptions Relaxed = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>The line under the detector table; <paramref name="keydetectLabel"/> is the keydetect build's label, where known.</summary>
    public string Note(string? keydetectLabel) =>
        keydetectLabel is { Length: > 0 }
            ? $"Read on keydetect {keydetectLabel}, after this verdict."
            : "Read after this verdict.";

    /// <summary>The evidence with the marker added; <paramref name="at"/> is null where the time is not known.</summary>
    public static string? Stamped(string json, DateTimeOffset? at)
    {
        if (JsonNode.Parse(json) is not JsonObject { } evidence)
            return null;

        evidence[Field] = new JsonObject { ["at"] = at?.ToString("o") };

        return evidence.ToJsonString(Relaxed);
    }

    /// <summary>The evidence without the marker, since every reading in it was seen by the verdict about to be recorded; unchanged when it has none or does not parse.</summary>
    public static string Unstamped(string json)
    {
        try
        {
            if (JsonNode.Parse(json) is not JsonObject { } evidence || !evidence.Remove(Field))
                return json;

            return evidence.ToJsonString(Relaxed);
        }
        catch (JsonException)
        {
            return json;
        }
    }

    /// <summary>Whether the evidence already carries the marker, or does not parse.</summary>
    public static bool IsStamped(string json) =>
        JsonNode.Parse(json) is not JsonObject { } evidence || evidence.ContainsKey(Field);
}
