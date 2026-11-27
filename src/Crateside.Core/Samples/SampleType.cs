using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Crateside.Core.Samples;

/// <summary>The part a sample plays in a mashup, carried as Ableton's <c>Type</c> keyword.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SampleType>))]
public enum SampleType
{
    Vocal,
    Instrumental,
}

public static partial class SampleTypes
{
    // Includes "no vocals" and "off vocal", which name an instrumental.
    [GeneratedRegex(
        @"\b(instrumentals?|inst\b|karaoke|(no|without|off)[\s-]vocals?)",
        RegexOptions.IgnoreCase
    )]
    private static partial Regex InstrumentalWords { get; }

    [GeneratedRegex(@"\b(a[\s-]?c+ap+el+a|vocals?|vox|voice only)\b", RegexOptions.IgnoreCase)]
    private static partial Regex VocalWords { get; }

    /// <summary>What an upload's title plainly says it is, or null when it does not say.</summary>
    public static SampleType? InferredFrom(string? title)
    {
        if (title is not { Length: > 0 })
            return null;

        var instrumental = InstrumentalWords.IsMatch(title);
        var vocal = VocalWords.IsMatch(InstrumentalWords.Replace(title, " "));

        return (vocal, instrumental) switch
        {
            (true, false) => SampleType.Vocal,
            (false, true) => SampleType.Instrumental,
            _ => null,
        };
    }

    /// <summary>The label a new file of this type is offered.</summary>
    public static string LabelFor(SampleType type) =>
        type switch
        {
            SampleType.Vocal => "A Cappella",
            _ => "Instrumental",
        };
}
