using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Crateside.Core.Ledger;
using Crateside.Core.Library;

namespace Crateside.Demo;

/// <summary>How far along the pipeline a demo track is.</summary>
internal enum DemoStage
{
    Waiting,
    Judging,
    Held,
    JudgedInLine,
    Separating,
    Parked,
    ReadyToWarp,
    ReadyToMove,
    Library,
}

/// <summary>One well-known song, and where the demo has it. The upload IDs are made up.</summary>
internal sealed record DemoTrack(
    string VideoId,
    string Artist,
    string Title,
    DemoStage Stage,
    double Bpm,
    int Seconds,
    string? Genre,
    string? Familiarity,
    double HoursAgo
)
{
    /// <summary>The loudness the stem was recorded at; null where the demo makes one up.</summary>
    public double? InstrumentalLufs { get; init; }

    public double? VocalLufs { get; init; }

    /// <summary>How the starter warp fared against the finished one; null leaves the song out of that panel.</summary>
    public StarterFate? Fate { get; init; }

    public int? Listeners { get; init; }

    public int? Pageviews { get; init; }

    /// <summary>What the lookups called the genre, before it was settled.</summary>
    public IReadOnlyList<string> Heard { get; init; } = [];

    public string Name => $"{Artist} - {Title}";

    public string Song => Name.ToLowerInvariant();

    public string Url => $"https://music.youtube.com/watch?v={VideoId}";

    /// <summary>What crateside recorded for the song; null for a track not judged yet.</summary>
    public Verdict? Verdict => HasVerdict ? Crateside.Demo.Verdict.Of(Artist, Title) : null;

    public KeyConfidence Confidence => Verdict?.Confidence ?? KeyConfidence.Unknown;

    public bool HasVerdict =>
        Stage
            is DemoStage.JudgedInLine
                or DemoStage.Separating
                or DemoStage.Parked
                or DemoStage.ReadyToWarp
                or DemoStage.ReadyToMove
                or DemoStage.Library;

    public bool HasStems =>
        Stage
            is DemoStage.Parked
                or DemoStage.ReadyToWarp
                or DemoStage.ReadyToMove
                or DemoStage.Library;

    /// <summary>The key a tag would carry, or null where the verdict gives none.</summary>
    public string? TaggedKey =>
        Verdict is { Confidence: KeyConfidence.High or KeyConfidence.Medium, Center: { } center }
            ? center.Collapsed
            : null;
}

internal static class DemoCast
{
    private static IReadOnlyList<DemoTrack> InFlight { get; } =
    [
        new(
            "DemoTrack04",
            "Michael Jackson",
            "Beat It",
            DemoStage.ReadyToWarp,
            139,
            258,
            "Rock",
            "Mainstream",
            96
        ),
        new(
            "DemoTrack06",
            "Britney Spears",
            "Toxic",
            DemoStage.ReadyToWarp,
            144,
            199,
            "Pop",
            "Mainstream",
            20
        ),
        new(
            "DemoTrack07",
            "Kesha",
            "TiK ToK",
            DemoStage.ReadyToWarp,
            120,
            200,
            "Pop",
            "Mainstream",
            14
        ),
        new(
            "DemoTrack08",
            "Whitney Houston",
            "How Will I Know",
            DemoStage.ReadyToWarp,
            118,
            276,
            "Pop",
            null,
            11
        ),
        new(
            "DemoTrack09",
            "Beyoncé",
            "Single Ladies (Put a Ring on It)",
            DemoStage.ReadyToWarp,
            96,
            193,
            "Pop",
            "Mainstream",
            8
        ),
        new(
            "DemoTrack10",
            "50 Cent",
            "Just A Lil Bit",
            DemoStage.Parked,
            97,
            237,
            "Hip Hop",
            null,
            6
        ),
        new(
            "DemoTrack11",
            "Rihanna",
            "Rude Boy",
            DemoStage.Separating,
            86.5,
            223,
            "Pop",
            null,
            2.5
        ),
        new(
            "DemoTrack12",
            "Katy Perry",
            "California Gurls",
            DemoStage.JudgedInLine,
            125,
            235,
            "Pop",
            null,
            1.8
        ),
        new(
            "DemoTrack13",
            "Mark Ronson, Bruno Mars",
            "Uptown Funk",
            DemoStage.JudgedInLine,
            115,
            270,
            "Pop",
            null,
            1.4
        ),
        new(
            "DemoTrack14",
            "Post Malone & Swae Lee",
            "Sunflower",
            DemoStage.Judging,
            90,
            158,
            "Hip Hop",
            null,
            0.6
        ),
        new("DemoTrack15", "Ed Sheeran", "Bad Habits", DemoStage.Held, 126, 231, "Pop", null, 0.9),
        new(
            "DemoTrack16",
            "Lady Gaga",
            "Bad Romance",
            DemoStage.Waiting,
            119,
            294,
            "Pop",
            null,
            0.3
        ),
        new(
            "DemoTrack17",
            "Pharrell Williams",
            "Happy",
            DemoStage.Waiting,
            160,
            233,
            "Pop",
            null,
            0.2
        ),
    ];

    /// <summary>The first track is the one the Song page shots show.</summary>
    public static IReadOnlyList<DemoTrack> All { get; } =
    [.. Library(DateTimeOffset.Now), .. InFlight];

    /// <summary>Hours back to the start of a track that finishes at its seed's place in its week; tagging ends about 40 minutes after the start.</summary>
    private static double HoursSince(
        DateTimeOffset now,
        DateTimeOffset thisWeek,
        LibrarySeed seed
    ) =>
        (now - thisWeek.AddDays(7 * (seed.Fraction!.Value - seed.WeeksAgo!.Value))).TotalHours
        + 0.65;

    /// <summary>
    /// The songs the library holds, as recorded for the same songs on a real machine (library.json), each finished
    /// in the week it names so the weekly history has room for it.
    /// </summary>
    private static List<DemoTrack> Library(DateTimeOffset now)
    {
        using var stream = Assembly
            .GetExecutingAssembly()
            .GetManifestResourceStream("library.json")!;
        var thisWeek = new DateTimeOffset(
            ISOWeek.ToDateTime(
                ISOWeek.GetYear(now.DateTime),
                ISOWeek.GetWeekOfYear(now.DateTime),
                DayOfWeek.Monday
            ),
            now.Offset
        );

        return
        [
            .. JsonSerializer
                .Deserialize(stream, LibrarySeedJsonContext.Default.ListLibrarySeed)!
                .Select(seed => new DemoTrack(
                    seed.VideoId,
                    seed.Artist,
                    seed.Title,
                    seed.Stage,
                    seed.Bpm,
                    seed.Seconds,
                    seed.Genre,
                    seed.Familiarity,
                    seed.HoursAgo ?? HoursSince(now, thisWeek, seed)
                )
                {
                    InstrumentalLufs = seed.InstrumentalLufs,
                    VocalLufs = seed.VocalLufs,
                    Fate = seed.Fate,
                    Listeners = seed.Listeners,
                    Pageviews = seed.Pageviews,
                    Heard = seed.Heard,
                }),
        ];
    }
}

/// <summary>One song of library.json.</summary>
internal sealed record LibrarySeed
{
    public required string VideoId { get; init; }
    public required string Artist { get; init; }
    public required string Title { get; init; }
    public required DemoStage Stage { get; init; }
    public required double Bpm { get; init; }
    public required int Seconds { get; init; }
    public string? Genre { get; init; }
    public string? Familiarity { get; init; }
    public double? InstrumentalLufs { get; init; }
    public double? VocalLufs { get; init; }
    public StarterFate? Fate { get; init; }
    public int? Listeners { get; init; }
    public int? Pageviews { get; init; }
    public IReadOnlyList<string> Heard { get; init; } = [];

    /// <summary>Hours before the demo was written, for a song that finished this week or last.</summary>
    public double? HoursAgo { get; init; }

    /// <summary>Whole ISO weeks before this one that the song finished, with <see cref="Fraction"/> of the way through that week.</summary>
    public int? WeeksAgo { get; init; }

    public double? Fraction { get; init; }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true
)]
[JsonSerializable(typeof(List<LibrarySeed>))]
internal sealed partial class LibrarySeedJsonContext : JsonSerializerContext { }
