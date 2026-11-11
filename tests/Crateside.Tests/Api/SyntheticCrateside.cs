using System.Text.Json;
using AbleKit.Analysis;
using AbleKit.Tags;
using Crateside.Api;
using Crateside.Core;
using Crateside.Core.Art;
using Crateside.Core.Familiarity;
using Crateside.Core.Genres;
using Crateside.Core.Ledger;
using Crateside.Core.Marks;
using Crateside.Core.Naming;
using Crateside.Core.Orchestration;
using Crateside.Core.Orchestration.Jobs;
using Crateside.Core.Orchestration.Loop;
using Crateside.Core.Orchestration.Verdict;
using Crateside.Core.Playlist;
using Crateside.Core.Tracks;
using Crateside.Core.Warps;
using Crateside.Core.WorkLists;
using Crateside.Tests.Tracks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Crateside.Tests.Api;

/// <summary>The API in process, over a temporary folder of synthetic state: one judged track with a warped stem, one waiting, one filed.</summary>
internal sealed class SyntheticCrateside : IAsyncDisposable
{
    public const string Judged = "Zx3TrackA01";
    public const string Waiting = "Zx3TrackB02";
    public const string Filed = "Zx3TrackC03";
    public const string Song = "the examples - synthetic song";

    private static readonly DateTimeOffset At = DateTimeOffset.Parse("2026-10-01T12:00:00+00:00");

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    private WebApplication? _app;
    private TracksService? _tracks;

    public HttpClient Client { get; private set; } = null!;

    public string Instrumental =>
        Path.Combine(_root, "Staging", "Synthetic Song (Instrumental - Full).flac");

    public string WarpBackups => Path.Combine(_root, "warp-backups");

    public static async Task<SyntheticCrateside> StartAsync()
    {
        var crateside = new SyntheticCrateside();
        await crateside.StartAppAsync();

        return crateside;
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        _tracks?.Dispose();

        if (_app is not null)
            await _app.DisposeAsync();

        Directory.Delete(_root, recursive: true);
    }

    public const string TheoryTab =
        "https://www.hooktheory.com/theorytab/view/the-examples/synthetic-song";

    /// <summary>The cited TheoryTab, and a cover's the judge passed over.</summary>
    private static readonly IReadOnlyList<SiteLookup> Lookups =
    [
        new(
            "Hooktheory",
            LookupStatus.Found,
            [
                new FetchedPage
                {
                    Site = "Hooktheory",
                    Url = TheoryTab,
                    Keys = [new PageKey("F# minor", "Verse", 126)],
                    Tab = new HooktheoryTab
                    {
                        Contributors = ["one", "two"],
                        Sections =
                        [
                            new TabSection("Verse", "i VI III VII", 100, 50)
                            {
                                Keys = [new TabKey(1, "F# minor")],
                                SyncStart = 0.1,
                                SyncEnd = 0.3,
                            },
                        ],
                        Coverage = 0.2,
                    },
                },
                new FetchedPage
                {
                    Site = "Hooktheory",
                    Url = "https://www.hooktheory.com/theorytab/view/a-cover-band/synthetic-song",
                    Keys = [new PageKey("G minor", "Chorus", 120)],
                },
            ]
        ),
        new("Musicnotes", LookupStatus.NotFound, []),
    ];

    private async Task StartAppAsync()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Instrumental)!);
        Directory.CreateDirectory(WarpBackups);

        var time = new FakeTimeProvider(At);
        var ledger = new LedgerStore(LedgerStore.InMemory, time);
        ledger.Record(
            new RunRecord
            {
                RecordedAt = At,
                Song = Song,
                Artist = "The Examples",
                Title = "Synthetic Song",
                SourceUrl = $"https://music.youtube.com/watch?v={Judged}",
                Confidence = KeyConfidence.High,
                Center = new TonalCenter("F#", DiatonicMode.Aeolian),
                Sources =
                [
                    new RunSource
                    {
                        Url = TheoryTab,
                        Kind = SourceKind.Authoritative,
                        StatesKey = "F# minor",
                    },
                ],
                Lookups = JsonSerializer.Serialize(
                    Lookups,
                    SiteLookupJsonContext.Default.IReadOnlyListSiteLookup
                ),
            }
        );
        ledger.Read().TryGetLatestByTrackKey(Judged, out var verdict);

        var written = new WrittenWarps();
        File.WriteAllBytes(Instrumental, [1, 2, 3]);
        Sidecar().Write($"{Instrumental}.asd");
        written.RecordStarter(
            Instrumental,
            new WrittenStarter(
                120,
                119.998,
                0.5,
                0,
                0.5,
                new ReadDownbeat(1.5, 0.317, 4),
                StarterBar.UnsureDownbeat,
                StarterGrid.DownbeatSureAt
            )
        );
        File.WriteAllBytes(
            Path.Combine(WarpBackups, $"2026-10-01 115900 {Path.GetFileName(Instrumental)}.asd"),
            [1]
        );
        File.WriteAllBytes(
            Path.Combine(
                WarpBackups,
                $"2026-10-01 115900 Other {Path.GetFileName(Instrumental)}.asd"
            ),
            [1]
        );

        var jobs = new JobStore(Path.Combine(_root, "jobs.jsonl"), time);
        jobs.Record(Judged, JobStage.Download, JobStatus.Running);
        jobs.Record(Judged, JobStage.Download, JobStatus.Succeeded);
        jobs.Record(Waiting, JobStage.Download, JobStatus.Queued);

        var genres = new GenreStore(ledger);
        genres.Hear(Judged, new GenreHearing(["synthwave"], null), At);

        var familiarity = new FamiliarityStore(ledger);
        familiarity.Measure(Song, new FamiliarityReading(1_200, 50_000, "Q42"), At, 1);

        var marks = new TrackMarkStore(ledger);
        marks.Set(Song, TrackMark.OddBars, marked: true);

        var exclusions = new MeasuringExclusionStore(ledger);
        exclusions.AddStretch(
            Song,
            new MeasuringStretch
            {
                StartSeconds = 6.5,
                EndSeconds = 9.5,
                Typed = "3–4",
                Reason = "Slows down and turns to chaos",
            }
        );

        var names = new ResolvedNameStore(Path.Combine(_root, "names.json"));
        names.Save(
            Waiting,
            new ResolvedName
            {
                DisplayTitle = "The Examples - Another Song",
                BaseName = "The Examples - Another Song",
            }
        );

        TracksView view = new()
        {
            Rows =
            [
                new TrackRow
                {
                    TrackKey = Judged,
                    Title = "The Examples - Synthetic Song",
                    Verdict = verdict,
                    Familiarity = familiarity.Read()[Song],
                    Marks = marks.Of(Song),
                    Samples =
                    [
                        new TrackSample
                        {
                            Path = Instrumental,
                            Label = "Instrumental - Full",
                            AbletonKey = "F#m",
                            HasDefaultClip = true,
                        },
                    ],
                },
                new TrackRow { TrackKey = Waiting, Title = "The Examples - Another Song" },
                new TrackRow
                {
                    TrackKey = Filed,
                    Title = "The Examples - Filed Song",
                    Samples =
                    [
                        new TrackSample
                        {
                            Path = Path.Combine(_root, "Library", "Filed Song (Vocal - Full).flac"),
                            Label = "Vocal - Full",
                            IsInLibrary = true,
                        },
                    ],
                },
            ],
            ReadAt = At,
        };

        _tracks = new TracksService(new TracksReads(() => view), new StageHub(), time);

        var details = new TrackDetailsReader(
            _tracks,
            ledger,
            jobs,
            new StageNotes(Path.Combine(_root, "stage-notes.jsonl"), time),
            new StemDetailsReader(
                new StemWarpReader(written, new TransientOrigins(), new LiveAnalyses()),
                written,
                new RetypedWarps(),
                new SidecarBackups(WarpBackups, time),
                new FolderInfoReader(),
                new FileIndexReader(Path.Combine(_root, "no-index"))
            ),
            new FiledRecordsReader(
                genres,
                familiarity,
                marks,
                exclusions,
                names,
                new TrackInstructionStore(ledger)
            ),
            new PageChoiceStore(ledger)
        );

        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = Environments.Production }
        );
        builder.WebHost.UseTestServer();
        builder.Services.AddCratesideApi();
        builder.Services.AddSingleton(_tracks);
        builder.Services.AddSingleton(details);
        builder.Services.AddSingleton<IStagesReader>(new CannedStages());
        builder.Services.AddSingleton(new MeasuringExclusionsReader(_tracks, exclusions));
        builder.Services.AddSingleton(
            new CoverArt(
                new ProcessRunner(),
                new CratesidePaths { ArtFolder = Path.Combine(_root, "art") }
            )
        );
        builder.Services.AddSingleton(
            new SourceIndex(
                new WorkingSet(
                    new CratesidePaths { WorkingFolder = Path.Combine(_root, "working") }
                ),
                Path.Combine(_root, "source-index.json")
            )
        );

        _app = builder.Build();
        _app.MapCratesideApi();
        _app.MapCoverArt();
        await _app.StartAsync(TestContext.Current.CancellationToken);

        Client = _app.GetTestClient();
    }

    /// <summary>Ten seconds, with a saved clip and Warp on: markers at 120 BPM from half a second in, four bars looped.</summary>
    private static AnalysisFile Sidecar() =>
        new()
        {
            Audio = AudioAnalysis.FromSamples(
                [.. Enumerable.Repeat(0.5f, 44_100 * 2 * 10)],
                2,
                [],
                3
            ),
            Warp = new Warp { Markers = [new(0.5, 0), new(2.5, 4), new(2.515625, 4.03125)] },
            Clip = new Clip
            {
                LoopStart = 0,
                LoopEnd = 16,
                OutMarker = 16,
                LoopOn = true,
            },
            IsDefaultClipSaved = true,
        };
}

/// <summary>Stands in for the stages reader, which needs the whole queue to build.</summary>
internal sealed class CannedStages : IStagesReader
{
    public Task<StagesView> ReadAsync(CancellationToken ct) =>
        Task.FromResult(
            new StagesView
            {
                Stages =
                [
                    new StageView
                    {
                        Stage = JobStage.Separation,
                        State = StageState.Idle,
                        Running = [],
                        Waiting =
                        [
                            new StageTrack
                            {
                                VideoId = "aaaaaaaaaaa",
                                Title = "Done already",
                                Picked = false,
                                Reason = SkipCause.AlreadyDone,
                            },
                        ],
                    },
                ],
            }
        );
}
