using Crateside.Core.Ledger;
using Crateside.Core.Orchestration;
using Crateside.Core.Orchestration.Edits;
using Crateside.Core.Plans;
using Crateside.Core.Playlist;
using Crateside.Core.Staging;
using Crateside.Core.WorkLists;
using Microsoft.Extensions.Options;

namespace Crateside.Tests.Orchestration.Edits;

/// <summary>Remove has to be able to finish (#33, ADR 0027).</summary>
public sealed class TrackRemovalTests : IDisposable
{
    private const string Video = "OwGuSPXPyX4";

    private readonly LedgerStore _ledger = new(LedgerStore.InMemory, TimeProvider.System);

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    private readonly string _staging;
    private readonly string _working;
    private readonly List<StagedFile> _staged = [];

    public TrackRemovalTests()
    {
        _staging = Path.Combine(_root, "Staging");
        _working = Path.Combine(_root, "working");

        Directory.CreateDirectory(_staging);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Removing_a_track_that_keeps_its_source_drops_the_instruction_before_the_file()
    {
        var settings = Settings();
        settings.SetPlan(Video, new StemPlan { Presets = [], KeepSource = true });
        var source = Stage("Rob Dougan - Clubbed to Death.flac");

        var outcome = Removal(settings).Apply(Video, [Video]);

        Assert.Null(outcome.Problem);
        Assert.Equal(1, outcome.Recycled);
        Assert.False(File.Exists(source));

        Assert.DoesNotContain(
            Settings().Read(),
            track => track.TrackKey == Video && track.Plan is not null
        );
    }

    [Fact]
    public void Both_records_go_so_the_next_scan_does_not_put_the_track_back()
    {
        var intake = Intake();
        intake.Record([new WorkItem { TrackKey = Video, Title = "Clubbed to Death" }]);

        Removal(Settings(), intake).Apply(Video, [Video]);

        Assert.Empty(Intake().Read());
    }

    [Fact]
    public void A_sample_the_user_built_by_hand_is_left_where_it_is()
    {
        var built = Stage(
            "Rob Dougan - Clubbed to Death (My Instrumental).flac",
            adopted: "My Instrumental"
        );
        var cut = Stage("Rob Dougan - Clubbed to Death (Vocal - Full).flac");

        var outcome = Removal(Settings()).Apply(Video, [Video]);

        Assert.Equal(1, outcome.Recycled);
        Assert.True(File.Exists(built));
        Assert.False(File.Exists(cut));
    }

    [Fact]
    public void The_working_set_goes_too()
    {
        var set = new WorkingSet(_working);
        File.WriteAllText(Path.Combine(set.Prepare(Video), "evidence.json"), "{}");

        Removal(Settings()).Apply(Video, [Video]);

        Assert.Empty(set.TrackKeys);
    }

    [Fact]
    public void What_would_be_recycled_is_known_before_anything_happens()
    {
        Stage("Rob Dougan - Clubbed to Death.flac");
        Stage("Rob Dougan - Clubbed to Death (Mine).flac", adopted: "Mine");

        var removal = Removal(Settings());

        Assert.Single(removal.Recycling([Video, Video]));
        Assert.Single(_staged, stem => File.Exists(stem.Path) && !stem.IsAdopted);
    }

    private TrackRemoval Removal(TrackInstructionStore settings, IntakeStore? intake = null) =>
        new(
            settings,
            intake ?? Intake(),
            new WorkingSet(_working),
            new ListedStagedFiles(() => _staged)
        );

    private TrackInstructionStore Settings() => new(_ledger);

    private IntakeStore Intake() => new(_ledger);

    private string Stage(string name, string? adopted = null)
    {
        var path = Path.Combine(_staging, name);

        File.WriteAllText(path, "stem");
        _staged.Add(
            new StagedFile
            {
                Path = path,
                FileName = name,
                AdoptedAs = adopted,
            }
        );

        return path;
    }
}
