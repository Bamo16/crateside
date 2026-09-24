using System.Text.Json;
using System.Text.Json.Serialization;
using Crateside.Core.Pairing;

namespace Crateside.Tests.Pairing;

/// <summary>
/// The port checked against the real output of <c>Get-MashupPartner -MaxShift 6</c>, the cmdlet
/// it was ported from, for every one of the 24 keys.
/// </summary>
public class PsMashupParityTests
{
    private static readonly IReadOnlyList<FixtureKey> Fixture = LoadFixture();

    public static TheoryData<string> SourceKeys => [.. Fixture.Select(f => f.Source)];

    private static IReadOnlyList<FixtureKey> LoadFixture()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "psmashup-partners.json");

        return JsonSerializer.Deserialize<List<FixtureKey>>(File.ReadAllText(path))
            ?? throw new InvalidOperationException($"Could not read the fixture at {path}.");
    }

    [Theory]
    [MemberData(nameof(SourceKeys))]
    public void Matches_the_cmdlet_for_every_source_key(string sourceKeyName)
    {
        var expected = Fixture.Single(f => f.Source == sourceKeyName);

        var source = MusicKey.FromString(sourceKeyName);
        Assert.NotNull(source);

        var actual = Partners.For(source);

        Assert.Equal(expected.Partners.Select(p => p.Key), actual.Select(p => p.Key.Name));

        foreach (var (want, got) in expected.Partners.Zip(actual))
        {
            Assert.Equal(want.Camelot, got.CamelotCode);
            Assert.Equal(want.Vibe, got.Relationship.ToDisplayString());
            Assert.Equal(want.Score, got.Score, precision: 6);
            Assert.Equal(want.Shift, got.Shift);
            Assert.Equal(want.AltShifts ?? string.Empty, got.AltShiftsDisplay);
        }
    }

    [Fact]
    public void The_fixture_covers_all_24_keys()
    {
        Assert.Equal(24, Fixture.Count);
        Assert.All(Fixture, f => Assert.Equal(24, f.Partners.Count));
    }

    private sealed record FixtureKey
    {
        [JsonPropertyName("source")]
        public required string Source { get; init; }

        [JsonPropertyName("partners")]
        public required List<FixturePartner> Partners { get; init; }
    }

    private sealed record FixturePartner
    {
        [JsonPropertyName("key")]
        public required string Key { get; init; }

        [JsonPropertyName("camelot")]
        public required string Camelot { get; init; }

        [JsonPropertyName("vibe")]
        public required string Vibe { get; init; }

        [JsonPropertyName("score")]
        public required double Score { get; init; }

        [JsonPropertyName("shift")]
        public required int Shift { get; init; }

        [JsonPropertyName("altShifts")]
        public string? AltShifts { get; init; }
    }
}
