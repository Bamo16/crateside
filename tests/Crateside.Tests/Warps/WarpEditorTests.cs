using AbleKit.Analysis;
using Crateside.Core;
using Crateside.Core.Ableton;
using Crateside.Core.Orchestration.Jobs;
using Crateside.Core.Warps;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Crateside.Tests.Warps;

public sealed class WarpEditorTests : IDisposable
{
    private const string Upload = "https://www.youtube.com/watch?v=abcdefghijk";

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    private readonly WrittenWarps _written = new();
    private readonly SavedWarps _saved = new();
    private readonly FakeTimeProvider _time = new(new(2026, 11, 17, 9, 0, 0, TimeSpan.Zero));
    private readonly CratesidePaths _paths;
    private readonly SidecarWriter _writer;

    public WarpEditorTests()
    {
        _paths = new CratesidePaths
        {
            StagingFolder = Path.Combine(_root, "Staging"),
            LibraryFolder = Path.Combine(_root, "Library"),
        };
        Directory.CreateDirectory(_paths.StagingFolder);
        Directory.CreateDirectory(_paths.LibraryFolder);
        _writer = new SidecarWriter(
            SteadyHits.Decoder,
            SteadyHits.Detector(),
            _written,
            new TransientOrigins()
        );
    }

    private string Instrumental =>
        Path.Combine(_paths.StagingFolder, "Song (Instrumental - Full).flac");

    private string Vocal => Path.Combine(_paths.StagingFolder, "Song (Vocal - Full).flac");

    private string Drums => Path.Combine(_paths.StagingFolder, "Song (Drums).flac");

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task A_save_with_no_draft_on_starter_warps_leaves_every_stem_the_users_own()
    {
        var (editor, siblings, plan) = await StarterGroup();
        Assert.All([Instrumental, Vocal, Drums], stem => Assert.True(_written.IsStarter(stem)));

        var outcome = await editor.SaveAsync(
            plan,
            siblings,
            new WarpDraft(),
            TestContext.Current.CancellationToken
        );

        Assert.IsType<WarpSaveOutcome.Saved>(outcome);
        Assert.All(
            [Instrumental, Vocal, Drums],
            stem =>
            {
                Assert.False(_written.IsCratesides(stem));
                Assert.False(_written.IsStarter(stem));
                Assert.True(AnalysisFile.Read($"{stem}.asd").IsDefaultClipSaved);
                Assert.NotNull(new WarpTempoReader((string?)null, _written).Read(stem));
                Assert.NotNull(_saved.Of(stem));
                var yours = Assert.IsType<WarpOwner.Yours>(Reader().Read(stem).Owner);
                Assert.NotNull(yours.SavedHere);
            }
        );
    }

    [Fact]
    public async Task A_saved_warp_is_kept_for_a_recut_and_counts_as_warped_for_the_handoff()
    {
        var (editor, siblings, plan) = await StarterGroup();

        await editor.SaveAsync(
            plan,
            siblings,
            new WarpDraft().Later(1),
            TestContext.Current.CancellationToken
        );
        new KeptWarps(_paths, _written).Keep(Instrumental);

        Assert.NotEmpty(
            Directory.EnumerateFiles(_paths.KeptWarpsFolder, "*", SearchOption.AllDirectories)
        );
    }

    [Fact]
    public async Task Saving_writes_the_draft_to_every_stem_of_the_group()
    {
        var (editor, siblings, plan) = await StarterGroup();
        var before = AnalysisFile.Read($"{Instrumental}.asd");

        await editor.SaveAsync(
            plan,
            siblings,
            new WarpDraft().Later(4).Nudged(5),
            TestContext.Current.CancellationToken
        );

        Assert.All(
            [Instrumental, Vocal, Drums],
            stem =>
            {
                var after = AnalysisFile.Read($"{stem}.asd");

                Assert.Equal(before.Warp.Markers[0].Beat - 4, after.Warp.Markers[0].Beat, 9);
                Assert.Equal(
                    before.Warp.Markers[0].Seconds + 0.005,
                    after.Warp.Markers[0].Seconds,
                    9
                );
            }
        );
    }

    [Fact]
    public async Task A_stem_saved_over_since_the_draft_began_stops_the_save_and_nothing_is_written()
    {
        var (editor, siblings, plan) = await StarterGroup();
        var vocalBefore = File.ReadAllBytes($"{Vocal}.asd");
        File.SetLastWriteTimeUtc($"{Instrumental}.asd", DateTime.UtcNow.AddMinutes(1));

        var outcome = await editor.SaveAsync(
            plan,
            siblings,
            new WarpDraft().Later(1),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(new WarpSaveOutcome.Changed(Instrumental), outcome);
        Assert.Equal(vocalBefore, File.ReadAllBytes($"{Vocal}.asd"));
    }

    [Fact]
    public async Task Undo_puts_every_stem_back_as_the_starter_it_was()
    {
        var (editor, siblings, plan) = await StarterGroup();
        var before = File.ReadAllBytes($"{Instrumental}.asd");
        var saved = Assert.IsType<WarpSaveOutcome.Saved>(
            await editor.SaveAsync(
                plan,
                siblings,
                new WarpDraft().Later(1),
                TestContext.Current.CancellationToken
            )
        );

        Assert.IsType<WarpUndoOutcome.Undone>(editor.Undo(saved.Save));

        Assert.Equal(before, File.ReadAllBytes($"{Instrumental}.asd"));
        Assert.All(
            [Instrumental, Vocal, Drums],
            stem =>
            {
                Assert.True(_written.IsStarter(stem));
                Assert.Null(_saved.Of(stem));
            }
        );
    }

    [Fact]
    public async Task Undo_does_nothing_once_a_stem_has_been_saved_over_in_Live()
    {
        var (editor, siblings, plan) = await StarterGroup();
        var saved = Assert.IsType<WarpSaveOutcome.Saved>(
            await editor.SaveAsync(
                plan,
                siblings,
                new WarpDraft().Later(1),
                TestContext.Current.CancellationToken
            )
        );
        File.SetLastWriteTimeUtc($"{Vocal}.asd", DateTime.UtcNow.AddMinutes(5));

        Assert.Equal(new WarpUndoOutcome.Changed(Vocal), editor.Undo(saved.Save));
        Assert.False(_written.IsStarter(Instrumental));
    }

    [Fact]
    public async Task A_save_is_worded_as_made_in_crateside_until_Live_saves_over_it()
    {
        var (editor, siblings, plan) = await StarterGroup();

        await editor.SaveAsync(
            plan,
            siblings,
            new WarpDraft().Later(1).Nudged(4),
            TestContext.Current.CancellationToken
        );
        var stem = Reader().Read(Instrumental);
        var wording = new WarpWording(WarpGroup.Of([stem]), path => Path.GetFileName(path));

        Assert.Contains("Saved in crateside on", wording.Sentence(stem));
        Assert.Contains("grid a beat later, +4 ms", wording.Sentence(stem));
        Assert.Contains("from crateside's starter", wording.Sentence(stem));

        File.SetLastWriteTimeUtc($"{Instrumental}.asd", DateTime.UtcNow.AddMinutes(5));

        Assert.Contains("you saved over it in Live", wording.Sentence(Reader().Read(Instrumental)));
    }

    private StemWarpReader Reader() =>
        new(_written, new TransientOrigins(), new LiveAnalyses(), _saved);

    private WarpEditor Editor()
    {
        var backups = new SidecarBackups(Path.Combine(_root, "backups"), _time);

        return new(
            _written,
            _saved,
            backups,
            new WarpActions(_writer, _written, new LiveAnalyses(), backups, _time),
            _time
        );
    }

    private async Task<(
        WarpEditor Editor,
        WarpSiblings Siblings,
        WarpSavePlan.Ready Plan
    )> StarterGroup()
    {
        Stem(Instrumental, "Instrumental - Full");
        Stem(Vocal, "Vocal - Full");
        Stem(Drums, "Drums - htdemucs_ft");

        var lending = new WarpLending(
            _paths,
            _writer,
            Options.Create(new WarpOptions { StarterWarps = StarterWarps.OnTheGrid }),
            new Grids(new StarterGrid(120, null, null))
        );
        await lending.LendAsync(TestContext.Current.CancellationToken);

        var siblings = WarpSiblings.Scan([_paths.StagingFolder]);
        var editor = Editor();
        var plan = Assert.IsType<WarpSavePlan.Ready>(
            editor.Plan(siblings, WarpSiblings.Read(Instrumental)!)
        );

        return (editor, siblings, plan);
    }

    private static void Stem(string path, string preset)
    {
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "silence.flac"), path);

        using var file = TagLib.File.Create(path);
        file.Tag.Comment = $"stemforge/0.4.0.0 | preset: {preset} | source: {Upload}";
        file.Save();
    }

    private sealed class Grids(StarterGrid grid) : IStarterGrids
    {
        public StarterGrid? Of(SiblingFile cut) => grid;
    }
}
