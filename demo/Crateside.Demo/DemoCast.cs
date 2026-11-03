using Crateside.Core.Ledger;

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
    string Genre,
    string? Familiarity,
    double HoursAgo
)
{
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
    /// <summary>The first track is the one the Song page shots show.</summary>
    public static IReadOnlyList<DemoTrack> All { get; } =
    [
        new(
            "DemoTrack01",
            "Taylor Swift",
            "Style",
            DemoStage.Library,
            95,
            231,
            "Pop",
            "Mainstream",
            170
        ),
        new(
            "DemoTrack02",
            "Adele",
            "Rolling in the Deep",
            DemoStage.Library,
            105,
            228,
            "Pop",
            "Mainstream",
            150
        ),
        new(
            "DemoTrack03",
            "Daft Punk ft. Pharrell Williams, Nile Rodgers",
            "Get Lucky",
            DemoStage.Library,
            116,
            248,
            "Pop",
            "Mainstream",
            120
        ),
        new(
            "DemoTrack04",
            "Michael Jackson",
            "Beat It",
            DemoStage.Library,
            139,
            258,
            "Rock",
            "Mainstream",
            96
        ),
        new(
            "DemoTrack05",
            "Eminem",
            "Without Me",
            DemoStage.ReadyToMove,
            112,
            290,
            "Hip Hop",
            "Mainstream",
            30
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
}
