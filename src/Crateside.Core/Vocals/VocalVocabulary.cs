using System.Diagnostics.CodeAnalysis;

namespace Crateside.Core.Vocals;

/// <summary>
/// The subtag a vocal stem takes in Live: <c>Type|Vocal|Lead</c> or <c>Type|Vocal|Sparse</c>
/// (ADR 0090).
/// </summary>
public static class VocalVocabulary
{
    public const string Lead = "Lead";
    public const string Sparse = "Sparse";

    private const string Prefix = "Type|Vocal|";

    public static IReadOnlyList<string> All { get; } = [Lead, Sparse];

    public static string Keyword(string value) => $"{Prefix}{value}";

    public static bool IsKeyword(string keyword) => TryParseKeyword(keyword, out _);

    public static bool TryParseKeyword(string keyword, [NotNullWhen(true)] out string? value)
    {
        value = keyword.StartsWith(Prefix, StringComparison.Ordinal)
            ? All.FirstOrDefault(known => keyword.AsSpan(Prefix.Length).SequenceEqual(known))
            : null;

        return value is not null;
    }
}
