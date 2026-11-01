using Crateside.Core.Ledger;
using Crateside.Core.Orchestration;
using Crateside.Core.Orchestration.Download;
using Crateside.Core.Orchestration.Jobs;
using Crateside.Core.Orchestration.Separation;
using Crateside.Core.Orchestration.Tagging;
using Crateside.Core.Plans;
using Crateside.Core.Staging;
using Crateside.Core.Tracks;
using Crateside.Core.WorkLists;
using Crateside.Tests.Ledger;

namespace Crateside.Tests.Tracks;

/// <summary>
/// The join that turns a playlist, a ledger, what is on disk and what crateside has run into one
/// row per track.
/// </summary>
public sealed class TracksViewCompositionTests : IDisposable
{
    private const string Video = "aaaaaaaaaaa";
    private const string FinishedRoot = @"D:\Music\Library";
    private const string StagingRoot = @"D:\Music\Sample Staging";

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    public TracksViewCompositionTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);

        GC.SuppressFinalize(this);
    }

    private JobStore Jobs() => new(Path.Combine(_root, "jobs.jsonl"));

    private KeyLedger Ledger()
    {
        var path = Path.Combine(_root, "runs.jsonl");
        File.WriteAllText(path, string.Empty);

        return TestLedger.From(path);
    }

    private KeyLedger LedgerWith(string key, string confidence, double bpm)
    {
        var path = Path.Combine(_root, "runs.jsonl");

        File.WriteAllText(
            path,
            System.Text.Json.JsonSerializer.Serialize(
                new
                {
                    timestamp = "2026-08-01T00:00:00+00:00",
                    song = "rob dougan - furious angels",
                    artist = "Rob Dougan",
                    title = "Furious Angels",
                    source_url = $"https://www.youtube.com/watch?v={Video}",
                    verdict = new { key, confidence },
                    bpm = new { value = bpm },
                }
            )
        );

        return TestLedger.From(path);
    }

    private static StagedSong Staged(params StagedFile[] stems) =>
        new()
        {
            SourceVideoId = Video,
            DisplayName = "Rob Dougan - Furious Angels",
            Stems = stems,
        };

    private static StagedFile Stem(string preset, bool tagged = false) =>
        new()
        {
            Path = Path.Combine(StagingRoot, $"track ({preset}).flac"),
            FileName = $"track ({preset}).flac",
            Preset = preset,
            AbletonKey = tagged ? "C minor" : null,
        };

    private static Dictionary<string, IReadOnlyList<StemOnDisk>> Finished(
        params string[] presets
    ) =>
        new(StringComparer.Ordinal)
        {
            [Video] =
            [
                .. presets.Select(preset => new StemOnDisk
                {
                    Path = Path.Combine(FinishedRoot, $"track ({preset}).flac"),
                    Root = FinishedRoot,
                    Preset = preset,
                }),
            ],
        };

    private static StemOnDisk Found(string preset, string root, DateTimeOffset? created = null) =>
        new()
        {
            Path = Path.Combine(root, $"track ({preset}).flac"),
            Root = root,
            Preset = preset,
            CreatedAt = created,
        };

    private static WorkItem Item(string videoId = Video) =>
        new()
        {
            PlaylistItemId = $"PLI-{videoId}",
            VideoId = videoId,
            Title = "Rob Dougan - Furious Angels",
        };

    private static WorkListsView View(
        IReadOnlyList<WorkItem>? items = null,
        IReadOnlySet<string>? verdictWithoutStems = null,
        IReadOnlySet<string>? separatedWithoutKey = null,
        params TrackInstruction[] instructions
    ) =>
        WorkListsView.Compose(
            items ?? [Item()],
            new HashSet<string>(),
            verdictWithoutStems ?? new HashSet<string>(),
            separatedWithoutKey ?? new HashSet<string>(),
            instructions.ToDictionary(t => t.VideoId, StringComparer.Ordinal)
        );

    private IReadOnlyList<TrackRow> Compose(
        WorkListsView? view = null,
        KeyLedger? ledger = null,
        IReadOnlyList<StagedSong>? staging = null,
        IReadOnlyDictionary<string, IReadOnlyList<StemOnDisk>>? onDisk = null,
        JobStore? jobs = null,
        IReadOnlyDictionary<string, Acquisition>? acquisitions = null,
        params TrackInstruction[] instructions
    ) =>
        TracksView
            .Compose(
                view ?? View(),
                ledger ?? Ledger(),
                staging ?? [],
                onDisk ?? new Dictionary<string, IReadOnlyList<StemOnDisk>>(),
                FinishedRoot,
                jobs ?? Jobs(),
                instructions.ToDictionary(t => t.VideoId, StringComparer.Ordinal),
                new TrackAnnotations
                {
                    Acquisitions = acquisitions ?? TrackAnnotations.None.Acquisitions,
                }
            )
            .Rows;

    private static WorkListsView InLineForTheGpu() =>
        View(verdictWithoutStems: new HashSet<string>(StringComparer.Ordinal) { Video });

    [Fact]
    public void A_judged_track_without_stems_is_in_line_for_the_gpu()
    {
        var row = Assert.Single(Compose(InLineForTheGpu()));

        Assert.Equal(TrackState.JudgedInLine, row.State);
        Assert.Equal(1, row.GpuPosition);
    }

    [Fact]
    public void A_track_already_separated_is_not_in_line_when_its_stems_cannot_be_found()
    {
        // Stems with no source in their tags join to no track, so it still reads as unseparated;
        // the drainer never redoes a success unasked, so the line must not claim it either.
        var jobs = Jobs();
        jobs.Record(Video, JobStage.Separation, JobStatus.Running);
        jobs.Record(Video, JobStage.Separation, JobStatus.Succeeded);

        var row = Assert.Single(Compose(InLineForTheGpu(), jobs: jobs));

        Assert.Null(row.GpuPosition);
        Assert.NotEqual(TrackState.JudgedInLine, row.State);
    }

    [Fact]
    public void A_track_separation_gave_up_on_shows_as_given_up_rather_than_in_line()
    {
        var jobs = Jobs();

        for (var i = 0; i < SeparationSelector.DefaultMaxStrikes; i++)
        {
            jobs.Record(Video, JobStage.Separation, JobStatus.Running);
            jobs.Record(Video, JobStage.Separation, JobStatus.Failed, error: "AudioExportError");
        }

        var row = Assert.Single(Compose(InLineForTheGpu(), jobs: jobs));

        Assert.Null(row.GpuPosition);
        Assert.Equal(TrackState.GaveUp, row.State);
    }

    [Fact]
    public void A_track_listed_twice_for_the_gpu_composes_into_one_row()
    {
        var item = Item();
        var view = new WorkListsView
        {
            Items = [item],
            Outstanding = [],
            FullyProcessed = [],
            AwaitingKey = [],
            AwaitingSeparation = [item, item],
        };

        var row = Assert.Single(Compose(view));

        Assert.Equal(1, row.GpuPosition);
    }

    [Fact]
    public void A_superseded_uploads_recycled_files_show_on_the_song_that_replaced_it()
    {
        const string Old = "ooooooooooo";

        // Two runs of one song, so the old upload resolves to this row's song after its files
        // are gone and nothing else joins it.
        var path = Path.Combine(_root, "runs.jsonl");
        File.WriteAllLines(
            path,
            [
                .. new[] { Old, Video }.Select(
                    (upload, index) =>
                        System.Text.Json.JsonSerializer.Serialize(
                            new
                            {
                                timestamp = $"2026-08-0{index + 1}T00:00:00+00:00",
                                song = "rob dougan - furious angels",
                                source_url = $"https://www.youtube.com/watch?v={upload}",
                                verdict = new { confidence = "WITHHELD" },
                            }
                        )
                ),
            ]
        );

        var row = Assert.Single(
            TracksView
                .Compose(
                    View(),
                    TestLedger.From(path),
                    [],
                    new Dictionary<string, IReadOnlyList<StemOnDisk>>(),
                    FinishedRoot,
                    Jobs(),
                    new Dictionary<string, TrackInstruction>(),
                    new TrackAnnotations
                    {
                        Deletions =
                        [
                            new Deletion
                            {
                                At = DateTimeOffset.Parse("2026-09-24T12:00:00+00:00"),
                                VideoId = Old,
                                FileName = "Rob Dougan - Furious Angels (Vocal - Full).flac",
                                Reason = "superseded by a source swap",
                            },
                        ],
                    }
                )
                .Rows
        );

        Assert.Equal(
            "Rob Dougan - Furious Angels (Vocal - Full).flac",
            Assert.Single(row.Deletions).FileName
        );
    }

    [Fact]
    public void Every_playlist_entry_becomes_exactly_one_row()
    {
        var row = Assert.Single(Compose());

        Assert.Equal(Video, row.VideoId);
        Assert.Equal("Rob Dougan - Furious Angels", row.Title);
        Assert.Equal(TrackState.Queued, row.State);
    }

    [Fact]
    public void A_parked_track_says_so_rather_than_looking_like_work_the_gpu_has_not_reached()
    {
        var row = Assert.Single(
            Compose(
                View(
                    verdictWithoutStems: new HashSet<string> { Video },
                    separatedWithoutKey: new HashSet<string> { Video }
                )
            )
        );

        Assert.Equal(TrackState.Parked, row.State);
        Assert.Equal(ParkReason.NoKey, row.ParkedFor);
    }

    [Fact]
    public void A_source_only_track_never_judged_waits_for_the_judge_rather_than_parking_for_a_key()
    {
        var row = Assert.Single(Compose(View(separatedWithoutKey: new HashSet<string> { Video })));

        Assert.Null(row.ParkedFor);
        Assert.True(row.WaitingForJudge);
        Assert.NotEqual(TrackState.Parked, row.State);
        Assert.Equal("waiting to be judged", row.ChipLabel);
    }

    [Fact]
    public void A_download_short_of_premium_parks_the_row_over_its_source()
    {
        var row = Assert.Single(
            Compose(
                acquisitions: new Dictionary<string, Acquisition>(StringComparer.Ordinal)
                {
                    [Video] = new() { PremiumStatus = PremiumStatus.NoPremiumLadder },
                }
            )
        );

        Assert.Equal(TrackState.Parked, row.State);
        Assert.Equal(ParkReason.PremiumShortfall, row.ParkedFor);
    }

    [Fact]
    public void A_never_judged_track_with_its_source_waiting_for_a_verdict_is_parked_over_a_poor_download_not_the_key()
    {
        var row = Assert.Single(
            Compose(
                View(separatedWithoutKey: new HashSet<string> { Video }),
                acquisitions: new Dictionary<string, Acquisition>(StringComparer.Ordinal)
                {
                    [Video] = new() { PremiumStatus = PremiumStatus.NoPremiumLadder },
                }
            )
        );

        Assert.Equal(ParkReason.PremiumShortfall, row.ParkedFor);
    }

    [Fact]
    public void A_download_that_got_premium_audio_leaves_the_row_alone()
    {
        // Recorded on every download, so the outcome counts, not the record's presence.
        var row = Assert.Single(
            Compose(
                acquisitions: new Dictionary<string, Acquisition>(StringComparer.Ordinal)
                {
                    [Video] = new() { PremiumStatus = PremiumStatus.Premium, IsPremium = true },
                }
            )
        );

        Assert.Equal(TrackState.Queued, row.State);
        Assert.Null(row.ParkedFor);
        Assert.NotNull(row.Acquisition);
    }

    [Fact]
    public void Want_of_a_key_outranks_a_shortfall_because_the_gpu_has_already_run()
    {
        var row = Assert.Single(
            Compose(
                View(
                    verdictWithoutStems: new HashSet<string> { Video },
                    separatedWithoutKey: new HashSet<string> { Video }
                ),
                acquisitions: new Dictionary<string, Acquisition>(StringComparer.Ordinal)
                {
                    [Video] = new() { PremiumStatus = PremiumStatus.NoPremiumLadder },
                }
            )
        );

        Assert.Equal(ParkReason.NoKey, row.ParkedFor);
    }

    [Fact]
    public void A_judged_track_is_numbered_by_its_place_in_the_line_for_the_gpu()
    {
        const string Second = "bbbbbbbbbbb";

        var rows = Compose(
            View([Item(), Item(Second)], verdictWithoutStems: new HashSet<string> { Video, Second })
        );

        Assert.Equal(1, rows[0].GpuPosition);
        Assert.Equal(2, rows[1].GpuPosition);
        Assert.Equal("next on the GPU", rows[0].Next);
        Assert.Equal("2nd for the GPU", rows[1].Next);
    }

    [Fact]
    public void The_ledger_names_the_track_where_the_playlist_only_titles_it()
    {
        // The playlist title omits the artist; the ledger keeps both.
        var row = Assert.Single(
            Compose(
                view: View([Item() with { Title = "Furious Angels (Official Video)" }]),
                ledger: LedgerWith("C minor", "HIGH", bpm: 128)
            )
        );

        Assert.Equal("Rob Dougan - Furious Angels", row.Title);
    }

    [Fact]
    public void An_unjudged_track_keeps_the_only_name_anything_knows()
    {
        var row = Assert.Single(Compose(view: View([Item() with { Title = "some upload" }])));

        Assert.Equal("some upload", row.Title);
    }

    [Fact]
    public void A_track_still_in_staging_keeps_its_row_after_leaving_the_playlist()
    {
        // Out of the playlist, still in staging waiting to be warped.
        var row = Assert.Single(
            Compose(view: View([]), staging: [Staged(Stem("Vocal - Full", tagged: true))])
        );

        Assert.Equal(Video, row.VideoId);
        Assert.Equal(TrackState.ReadyToWarp, row.State);

        Assert.Null(row.PlaylistItemId);
    }

    [Fact]
    public void A_staged_track_that_is_still_queued_gets_one_row_and_not_two()
    {
        var rows = Compose(staging: [Staged(Stem("Vocal - Full", tagged: true))]);

        Assert.Equal(Video, Assert.Single(rows).VideoId);
        Assert.Equal($"PLI-{Video}", rows[0].PlaylistItemId);
    }

    [Fact]
    public void The_verdict_supplies_the_key_the_confidence_and_the_bpm()
    {
        var row = Assert.Single(Compose(ledger: LedgerWith("C minor", "HIGH", bpm: 128)));

        Assert.Equal("C minor", row.Key);
        Assert.Equal(KeyConfidence.High, row.Confidence);
        Assert.Equal(128, row.Bpm?.Value);
    }

    [Fact]
    public void A_verdict_too_weak_to_tag_with_still_carries_its_bpm()
    {
        var row = Assert.Single(Compose(ledger: LedgerWith("C minor", "LOW", bpm: 128)));

        Assert.Null(row.Key);
        Assert.Equal(KeyConfidence.Low, row.Confidence);
        Assert.Equal(128, row.Bpm?.Value);
    }

    [Fact]
    public void A_staged_tracks_samples_and_their_tags_come_from_the_board()
    {
        var row = Assert.Single(
            Compose(
                staging:
                [
                    Staged(
                        Stem("Vocal - Full", tagged: true),
                        Stem("Instrumental - Full"),
                        Stem("Drums", tagged: true)
                    ),
                ]
            )
        );

        // The warp guide is not a sample (ADR 0004).
        Assert.Equal(2, row.Samples.Count);
        Assert.True(row.HasWarpGuide);
        Assert.Contains(
            row.Samples,
            sample => sample is { Label: "Vocal - Full", IsKeyTagged: true }
        );
        Assert.DoesNotContain(row.Samples, sample => sample.IsInLibrary);
        Assert.Equal(TrackState.Tagging, row.State);
    }

    [Fact]
    public void An_instruction_outlives_the_playlist_entry_it_was_given_for()
    {
        var row = Assert.Single(
            Compose(
                view: View(items: []),
                staging: [Staged(Stem("Drums"))],
                instructions: new TrackInstruction
                {
                    VideoId = Video,
                    Plan = StemPlan.InstrumentalRelease,
                }
            )
        );

        Assert.Null(row.PlaylistItemId);
        Assert.True(row.Plan.AsksTheSameAs(StemPlan.InstrumentalRelease));
        Assert.Equal(SeparationRecipe.InstrumentalRelease, row.Recipe);
    }

    [Fact]
    public void A_staged_samples_tag_is_the_key_Ableton_holds()
    {
        var row = Assert.Single(
            Compose(
                ledger: LedgerWith("C minor", "HIGH", bpm: 128),
                staging: [Staged(Stem("Vocal - Full", tagged: true), Stem("Instrumental - Full"))]
            )
        );

        Assert.Contains(row.Samples, sample => sample.AbletonKey is "C minor");
        Assert.Contains(row.Samples, sample => sample is { AbletonKey: null, IsInLibrary: false });
    }

    [Fact]
    public void A_warped_sample_carries_no_Ableton_key_because_nothing_read_one()
    {
        // Null here says nobody looked, not that the file is untagged.
        var row = Assert.Single(Compose(onDisk: Finished("Vocal - Full")));

        Assert.All(row.Samples, sample => Assert.Null(sample.AbletonKey));
    }

    [Fact]
    public void The_folder_is_staging_while_anything_is_still_there()
    {
        var row = Assert.Single(
            Compose(
                staging: [Staged(Stem("Vocal - Full"))],
                onDisk: Finished("Instrumental - Full")
            )
        );

        Assert.Equal(StagingRoot, row.Folder);
    }

    [Fact]
    public void The_folder_is_wherever_the_files_got_to_once_staging_is_empty()
    {
        var row = Assert.Single(Compose(onDisk: Finished("Vocal - Full")));

        Assert.Equal(FinishedRoot, row.Folder);
    }

    [Fact]
    public void A_track_with_nothing_on_disk_has_no_folder()
    {
        Assert.Null(Assert.Single(Compose()).Folder);
    }

    [Fact]
    public void A_sample_found_in_the_library_is_warped_and_finishes_the_track()
    {
        var row = Assert.Single(Compose(onDisk: Finished("Vocal - Full", "Instrumental - Full")));

        Assert.Equal(2, row.Samples.Count);
        Assert.All(row.Samples, sample => Assert.True(sample.IsInLibrary));
        Assert.Equal(TrackState.Done, row.State);
    }

    [Fact]
    public void Half_a_track_moved_out_is_still_the_users_turn()
    {
        // Each file is under exactly one root, so neither is counted twice.
        var row = Assert.Single(
            Compose(
                staging: [Staged(Stem("Instrumental - Full", tagged: true), Stem("Drums"))],
                onDisk: Finished("Vocal - Full")
            )
        );

        Assert.Equal(2, row.Samples.Count);
        Assert.Equal(TrackState.ReadyToWarp, row.State);
        Assert.True(row.HasWarpGuide);
    }

    [Fact]
    public void A_stage_in_flight_outranks_what_is_on_disk()
    {
        var jobs = Jobs();
        jobs.Record(Video, JobStage.Separation, JobStatus.Running);

        var row = Assert.Single(
            Compose(staging: [Staged(Stem("Vocal - Full", tagged: true))], jobs: jobs)
        );

        Assert.Equal(JobStage.Separation, row.RunningStage);
        Assert.Equal(TrackState.Separating, row.State);
    }

    [Fact]
    public void A_stage_still_retrying_reaches_the_row()
    {
        var jobs = Jobs();
        jobs.Record(Video, JobStage.Handoff, JobStatus.Failed, error: "file in use");

        var row = Assert.Single(Compose(jobs: jobs));

        Assert.Equal(new StageFailure(JobStage.Handoff, 1, "file in use"), row.Retrying);
    }

    [Fact]
    public void A_stage_that_gave_up_reaches_the_row_with_its_count()
    {
        var jobs = Jobs();

        for (var attempt = 0; attempt < TaggingSelector.DefaultMaxStrikes; attempt++)
            jobs.Record(Video, JobStage.Tagging, JobStatus.Failed, error: "Ableton had the file");

        var row = Assert.Single(Compose(jobs: jobs));

        Assert.Equal(JobStage.Tagging, row.GaveUpAt);
        Assert.Equal(TaggingSelector.DefaultMaxStrikes, row.GaveUpStrikes);
        Assert.Equal(TrackState.GaveUp, row.State);
    }

    [Fact]
    public void A_staged_track_is_dated_by_when_the_earliest_of_its_files_arrived()
    {
        var arrived = new DateTimeOffset(2026, 8, 12, 9, 0, 0, TimeSpan.Zero);

        var row = Assert.Single(
            Compose(
                onDisk: new Dictionary<string, IReadOnlyList<StemOnDisk>>(StringComparer.Ordinal)
                {
                    [Video] =
                    [
                        Found("Vocal - Full", StagingRoot, arrived.AddMinutes(4)),
                        Found("Instrumental - Full", StagingRoot, arrived),
                    ],
                }
            )
        );

        Assert.Equal(arrived, row.StagedSince);
    }

    [Fact]
    public void A_track_whose_files_have_all_left_is_no_longer_staged_since_anything()
    {
        var row = Assert.Single(
            Compose(
                onDisk: new Dictionary<string, IReadOnlyList<StemOnDisk>>(StringComparer.Ordinal)
                {
                    [Video] = [Found("Vocal - Full", FinishedRoot, DateTimeOffset.UtcNow)],
                }
            )
        );

        Assert.Null(row.StagedSince);
    }

    [Fact]
    public void The_files_name_the_track_even_when_the_ledger_has_a_better_name()
    {
        var row = Assert.Single(
            Compose(
                View([]),
                ledger: LedgerWith("C minor", "HIGH", 120),
                staging: [Staged(Stem("Vocal - Full"))]
            )
        );

        Assert.Equal(TrackNameSource.Files, row.NameSource);
        Assert.Equal("track", row.Title);
    }

    [Fact]
    public void A_track_with_nothing_on_disk_falls_back_to_the_ledger_then_the_playlist()
    {
        var fromLedger = Assert.Single(Compose(ledger: LedgerWith("C minor", "HIGH", 120)));

        Assert.Equal(TrackNameSource.Ledger, fromLedger.NameSource);
        Assert.Equal("Rob Dougan - Furious Angels", fromLedger.Title);
        Assert.Equal(TrackNameSource.Playlist, Assert.Single(Compose()).NameSource);
    }

    [Fact]
    public void A_library_only_row_is_named_by_its_files_rather_than_by_a_video_id()
    {
        var row = Assert.Single(Compose(View([]), onDisk: Finished("Vocal - Full")));

        Assert.Equal(TrackNameSource.Files, row.NameSource);
        Assert.Equal("track", row.Title);
    }

    [Fact]
    public void Files_that_disagree_are_settled_by_the_most_common_name()
    {
        // A rename that stopped part way.
        var row = Assert.Single(
            Compose(
                View([]),
                staging:
                [
                    Staged(Stem("Vocal - Full")) with
                    {
                        Stems =
                        [
                            Stem("Vocal - Full"),
                            Stem("Instrumental - Full"),
                            Stem("Drums") with
                            {
                                Path = Path.Combine(StagingRoot, "left behind (Drums).flac"),
                            },
                        ],
                    },
                ]
            )
        );

        Assert.Equal("track", row.Title);
    }

    [Fact]
    public void A_stated_name_beats_every_derived_one_and_keeps_what_it_replaced()
    {
        var row = Assert.Single(
            Compose(
                ledger: LedgerWith("C minor", "HIGH", 120),
                instructions: new TrackInstruction { VideoId = Video, Name = "Better Name" }
            )
        );

        Assert.Equal("Better Name", row.Title);
        Assert.True(row.IsRenamed);

        Assert.Equal("Rob Dougan - Furious Angels", row.DerivedTitle);
    }

    [Fact]
    public void A_track_nobody_renamed_carries_no_name_to_revert_to()
    {
        var row = Assert.Single(Compose(ledger: LedgerWith("C minor", "HIGH", 120)));

        Assert.False(row.IsRenamed);
        Assert.Null(row.DerivedTitle);
    }

    [Fact]
    public void A_staged_file_not_carrying_the_stated_name_is_a_rename_left_unfinished()
    {
        // A file Ableton had open when the rename ran.
        var row = Assert.Single(
            Compose(
                View([]),
                staging: [Staged(Stem("Vocal - Full"), Stem("Drums"))],
                instructions: new TrackInstruction { VideoId = Video, Name = "track" }
            )
        );

        // The fixture names its files "track (<preset>).flac", so a stated name of "track" is
        // already carried by both.
        Assert.Equal(0, row.AwaitingRename);

        var stale = Assert.Single(
            Compose(
                View([]),
                staging: [Staged(Stem("Vocal - Full"), Stem("Drums"))],
                instructions: new TrackInstruction { VideoId = Video, Name = "Something Else" }
            )
        );

        Assert.Equal(2, stale.AwaitingRename);
        Assert.True(stale.Matches(TrackFilter.NeedsAttention));
    }

    [Fact]
    public void A_track_nobody_renamed_is_never_half_renamed()
    {
        // Measured against a stated name only, and there is none here.
        var row = Assert.Single(
            Compose(
                View([]),
                ledger: LedgerWith("C minor", "HIGH", 120),
                staging: [Staged(Stem("Vocal - Full"))]
            )
        );

        Assert.Equal(0, row.AwaitingRename);
        Assert.False(row.Matches(TrackFilter.NeedsAttention));
    }

    [Fact]
    public void A_file_in_the_library_is_part_of_what_a_rename_touches()
    {
        var partly = Assert.Single(
            Compose(
                View([]),
                staging: [Staged(Stem("Vocal - Full"))],
                onDisk: new Dictionary<string, IReadOnlyList<StemOnDisk>>(StringComparer.Ordinal)
                {
                    [Video] = [Found("Instrumental - Full", FinishedRoot)],
                }
            )
        );

        Assert.Equal("2 files + folder info", partly.RenameScope);
        Assert.Contains("Staging and the library", partly.RenameDetail);
    }

    [Fact]
    public void What_a_rename_would_touch_is_counted_off_the_row()
    {
        Assert.Equal("at staging", Assert.Single(Compose()).RenameScope);

        var staged = Assert.Single(
            Compose(View([]), staging: [Staged(Stem("Vocal - Full"), Stem("Drums"))])
        );

        Assert.Equal("2 files + folder info", staged.RenameScope);
    }
}
