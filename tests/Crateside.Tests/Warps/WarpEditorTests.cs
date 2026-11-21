using AbleKit.Analysis;
using Crateside.Core;
using Crateside.Core.Ableton;
using Crateside.Core.SourceOffsets;
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
            ct: TestContext.Current.CancellationToken
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
            ct: TestContext.Current.CancellationToken
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
            ct: TestContext.Current.CancellationToken
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
    public async Task A_save_reaches_a_lent_drums_stem_after_the_instrumental_moved_to_the_library()
    {
        await StarterGroup();
        _written.RecordBorrowed(Drums, Instrumental);
        var moved = Path.Combine(_paths.LibraryFolder, Path.GetFileName(Instrumental));
        File.Move(Instrumental, moved);
        File.Move($"{Instrumental}.asd", $"{moved}.asd");
        var siblings = WarpSiblings.Scan([_paths.StagingFolder, _paths.LibraryFolder]);
        var editor = Editor();
        var plan = Assert.IsType<WarpSavePlan.Ready>(
            editor.Plan(siblings, WarpSiblings.Read(moved)!)
        );
        var before = AnalysisFile.Read($"{Drums}.asd");

        await editor.SaveAsync(
            plan,
            siblings,
            new WarpDraft().Later(4),
            ct: TestContext.Current.CancellationToken
        );

        Assert.Equal(
            before.Warp.Markers[0].Beat - 4,
            AnalysisFile.Read($"{Drums}.asd").Warp.Markers[0].Beat,
            9
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
            ct: TestContext.Current.CancellationToken
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
                ct: TestContext.Current.CancellationToken
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
                ct: TestContext.Current.CancellationToken
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
            ct: TestContext.Current.CancellationToken
        );
        var stem = Reader().Read(Instrumental);
        var wording = new WarpWording(WarpGroup.Of([stem]), path => Path.GetFileName(path));

        Assert.Contains("Saved in crateside on", wording.Sentence(stem));
        Assert.Contains("bar a beat later, +4 ms", wording.Sentence(stem));
        Assert.Contains("from crateside's auto warp", wording.Sentence(stem));

        File.SetLastWriteTimeUtc($"{Instrumental}.asd", DateTime.UtcNow.AddMinutes(5));

        Assert.Contains("you saved over it in Live", wording.Sentence(Reader().Read(Instrumental)));
    }

    [Fact]
    public async Task An_aligned_added_source_is_written_the_mixs_warp_shifted_by_its_offset_and_backed_up()
    {
        var (editor, siblings) = await AddedGroup(OffsetVerdict.Aligned);
        var before = File.ReadAllBytes($"{Extra}.asd");

        var outcome = await editor.SaveAsync(
            _plan,
            siblings,
            new WarpDraft().Later(1),
            ct: TestContext.Current.CancellationToken
        );

        var save = Assert.IsType<WarpSaveOutcome.Saved>(outcome).Save;
        var mix = AnalysisFile.Read($"{Instrumental}.asd");
        var shifted = AnalysisFile.Read($"{Extra}.asd");
        var one = Assert.Single(save.Shifted);

        Assert.Equal(
            mix.Warp.SecondsAt(8)!.Value - ExtraShift,
            shifted.Warp.SecondsAt(8)!.Value,
            1e-6
        );
        Assert.Equal(before, File.ReadAllBytes(one.Backup));
        Assert.NotEqual(before, File.ReadAllBytes($"{Extra}.asd"));
        Assert.IsType<WarpOwner.Shifted>(Reader().Read(Extra).Owner);
        Assert.DoesNotContain(Extra, _written.LentTo(Instrumental));
    }

    [Fact]
    public async Task Undo_puts_a_shifted_added_source_back_as_it_was()
    {
        var (editor, siblings) = await AddedGroup(OffsetVerdict.Aligned);
        var before = File.ReadAllBytes($"{Extra}.asd");
        var save = Assert
            .IsType<WarpSaveOutcome.Saved>(
                await editor.SaveAsync(
                    _plan,
                    siblings,
                    new WarpDraft().Later(1),
                    ct: TestContext.Current.CancellationToken
                )
            )
            .Save;

        editor.Undo(save);

        Assert.Equal(before, File.ReadAllBytes($"{Extra}.asd"));
        Assert.Null(_written.Of(Extra)?.Shift);
    }

    [Fact]
    public async Task An_added_source_holding_its_own_warp_is_left_alone()
    {
        var (editor, siblings) = await AddedGroup(OffsetVerdict.Aligned, recorded: false);
        var before = File.ReadAllBytes($"{Extra}.asd");

        var save = Assert
            .IsType<WarpSaveOutcome.Saved>(
                await editor.SaveAsync(
                    _plan,
                    siblings,
                    new WarpDraft().Later(1),
                    ct: TestContext.Current.CancellationToken
                )
            )
            .Save;

        Assert.Equal(AddedSkip.HasOwnWarp, _plan.Added.Single().Skip);
        Assert.Empty(save.Shifted);
        Assert.Equal(before, File.ReadAllBytes($"{Extra}.asd"));
    }

    [Theory]
    [InlineData(OffsetVerdict.Edit, AddedSkip.Edit)]
    [InlineData(OffsetVerdict.Drift, AddedSkip.Drift)]
    [InlineData(OffsetVerdict.NotTheSameRecording, AddedSkip.NotTheSameRecording)]
    public async Task An_added_source_the_measurement_refused_is_not_written(
        OffsetVerdict verdict,
        AddedSkip skip
    )
    {
        var (editor, siblings) = await AddedGroup(verdict);
        var before = File.ReadAllBytes($"{Extra}.asd");

        await editor.SaveAsync(
            _plan,
            siblings,
            new WarpDraft().Later(1),
            ct: TestContext.Current.CancellationToken
        );

        Assert.Equal(skip, _plan.Added.Single().Skip);
        Assert.Equal(before, File.ReadAllBytes($"{Extra}.asd"));
    }

    [Fact]
    public async Task An_added_source_not_measured_yet_waits_and_is_not_written()
    {
        var (editor, siblings) = await AddedGroup(null);

        await editor.SaveAsync(
            _plan,
            siblings,
            new WarpDraft().Later(1),
            ct: TestContext.Current.CancellationToken
        );

        Assert.Equal(AddedSkip.Measuring, _plan.Added.Single().Skip);
    }

    [Fact]
    public async Task An_added_source_saved_over_since_the_draft_began_stops_the_whole_save()
    {
        var (editor, siblings) = await AddedGroup(OffsetVerdict.Aligned);
        var stems = new[] { Instrumental, Vocal, Drums }
            .Select(stem => File.ReadAllBytes($"{stem}.asd"))
            .ToArray();

        File.SetLastWriteTimeUtc($"{Extra}.asd", DateTime.UtcNow.AddMinutes(5));

        var outcome = await editor.SaveAsync(
            _plan,
            siblings,
            new WarpDraft().Later(1),
            ct: TestContext.Current.CancellationToken
        );

        Assert.IsType<WarpSaveOutcome.Changed>(outcome);
        Assert.Equal(
            stems,
            new[] { Instrumental, Vocal, Drums }.Select(stem => File.ReadAllBytes($"{stem}.asd"))
        );
    }

    [Fact]
    public async Task A_source_that_starts_after_the_mixs_clip_gets_a_clip_start_on_the_next_bar_line()
    {
        var (_, siblings, plan) = await StarterGroup();
        await Editor()
            .SaveAsync(plan, siblings, new WarpDraft(), ct: TestContext.Current.CancellationToken);
        var mix = AnalysisFile.Read($"{Instrumental}.asd");

        var shifted = ShiftedWarp.Derive(mix, mix, 3 * 44_100, 44_100, 60);

        Assert.NotNull(shifted);
        Assert.True(shifted.ClipStartMoved);
        Assert.True(shifted.ClipStartNowBar > shifted.ClipStartWasBar);
        Assert.Equal(0, shifted.File.Warp.Markers[0].Seconds);
        Assert.Equal(0, shifted.File.Clip.LoopStart % 4);
        Assert.True(shifted.File.Clip.LoopStart >= shifted.File.Warp.Markers[0].Beat);
        Assert.False(shifted.File.Clip.LoopOn);
    }

    [Fact]
    public async Task A_source_shorter_than_the_mix_ends_on_a_hidden_marker_inside_the_file()
    {
        var (_, siblings, plan) = await StarterGroup();
        await Editor()
            .SaveAsync(plan, siblings, new WarpDraft(), ct: TestContext.Current.CancellationToken);
        var mix = AnalysisFile.Read($"{Instrumental}.asd");

        var shifted = ShiftedWarp.Derive(mix, mix, 0, 44_100, 10);

        Assert.NotNull(shifted);
        Assert.All(shifted.File.Warp.Markers, marker => Assert.InRange(marker.Seconds, 0, 10.1));
        Assert.InRange(shifted.File.Clip.LoopEnd, 19, 20);
    }

    private const double ExtraShift = 0.1;

    private WarpSavePlan.Ready _plan = null!;

    private string Extra => Path.Combine(_paths.StagingFolder, "Song (Official Instrumental).flac");

    /// <summary>Plans a save with one added source whose measurement said <paramref name="verdict"/> (null: not measured).</summary>
    private async Task<(WarpEditor Editor, WarpSiblings Siblings)> AddedGroup(
        OffsetVerdict? verdict,
        bool recorded = true
    )
    {
        var (editor, siblings, _) = await StarterGroup();
        var asd = $"{Extra}.asd";

        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "silence.flac"), Extra);
        (AnalysisFile.Read($"{Instrumental}.asd") with { IsDefaultClipSaved = true }).Write(asd);

        if (recorded)
            _written.RecordStarter(Extra);

        var offsets = new SourceOffsetStore();

        if (verdict is { } measured)
            offsets.Save(
                "added",
                new SourceOffsetRecord(
                    "main",
                    FileStamp.Of(Instrumental)!,
                    FileStamp.Of(Extra)!,
                    new OffsetReading
                    {
                        Verdict = measured,
                        SampleRate = 44_100,
                        WindowSeconds = 5,
                        Offset =
                            measured is OffsetVerdict.Aligned ? (int)(ExtraShift * 44_100) : null,
                    },
                    _time.GetUtcNow()
                )
            );

        var backups = new SidecarBackups(Path.Combine(_root, "backups"), _time);
        var withOffsets = new WarpEditor(
            _written,
            _saved,
            backups,
            new WarpActions(_writer, _written, new LiveAnalyses(), backups, _time),
            _time,
            offsets
        );

        _plan = Assert.IsType<WarpSavePlan.Ready>(
            withOffsets.Plan(
                siblings,
                WarpSiblings.Read(Instrumental)!,
                [new AddedSourceFile(Extra, "added", "Official Instrumental")]
            )
        );

        return (withOffsets, siblings);
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
