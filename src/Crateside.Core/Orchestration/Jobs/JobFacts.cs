using System.Globalization;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Humanizer;

namespace Crateside.Core.Orchestration.Jobs;

/// <summary>What a finished stage's summary is worded from, stored as numbers so a wording change reaches old lines (#197).</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(JudgeFacts), "judge")]
[JsonDerivedType(typeof(HandoffFacts), "handoff")]
[JsonDerivedType(typeof(TaggingFacts), "tagging")]
public abstract record JobFacts
{
    public abstract string Text { get; }
}

/// <param name="Key">The key judged, or null when it was withheld.</param>
/// <param name="Confidence">As the ledger names it.</param>
public sealed record JudgeFacts(
    string? Key,
    string Confidence,
    int Sections,
    int Turns,
    string Model,
    double Seconds,
    long InputTokens,
    long CachedTokens,
    long OutputTokens
) : JobFacts
{
    [JsonIgnore]
    public override string Text =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{Key ?? "key withheld"}, {Confidence.ToUpperInvariant()}, {"section".ToQuantity(Sections)}; {"turn".ToQuantity(Turns)} on {Model}, {Seconds:0} s{Tokens(InputTokens, CachedTokens, OutputTokens)}"
        );

    /// <summary>", 30k in (27k cached) · 1.6k out", or empty when the endpoint reported no usage.</summary>
    internal static string Tokens(long input, long cached, long output) =>
        input + output is 0
            ? string.Empty
            : $", {Abbreviate(input)} in ({Abbreviate(cached)} cached) · {Abbreviate(output)} out";

    private static string Abbreviate(long tokens) =>
        tokens switch
        {
            < 1000 => tokens.ToString(CultureInfo.InvariantCulture),
            < 10_000 => $"{(tokens / 1000.0).ToString("0.0", CultureInfo.InvariantCulture)}k",
            _ => $"{Math.Round(tokens / 1000.0).ToString(CultureInfo.InvariantCulture)}k",
        };
}

public sealed record HandoffFacts(int Moved, int Cleared) : JobFacts
{
    [JsonIgnore]
    public override string Text =>
        Cleared is 0
            ? $"staged {"stem".ToQuantity(Moved)}"
            : $"staged {"stem".ToQuantity(Moved)}, removed {"superseded file".ToQuantity(Cleared)}";
}

/// <param name="Key">The one key every file carries, or null when they differ.</param>
public sealed record TaggingFacts(int Applied, string? Key) : JobFacts
{
    [JsonIgnore]
    public override string Text =>
        Key is null
            ? $"tagged {"file".ToQuantity(Applied)}"
            : $"tagged {"file".ToQuantity(Applied)} {Key}";
}

/// <summary>Judge summaries written before e88ca40 end in a dollar figure, which is not shown any more.</summary>
internal static partial class LegacyJudgeText
{
    public static string WithoutDollars(string text) => DollarClause.Replace(text, string.Empty);

    [GeneratedRegex(@",\s*\$\d+(\.\d+)?")]
    private static partial Regex DollarClause { get; }
}
