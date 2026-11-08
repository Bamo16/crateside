using AbleKit.Tags;
using Crateside.Core;
using Crateside.Core.Beats;
using Crateside.Core.Familiarity;
using Crateside.Core.Genres;
using Crateside.Core.Ledger;
using Crateside.Core.Loudness;
using Crateside.Core.Marks;
using Crateside.Core.Naming;
using Crateside.Core.Orchestration.Jobs;
using Crateside.Core.Orchestration.Loop;
using Crateside.Core.Playlist;
using Crateside.Core.Samples;
using Crateside.Core.WorkLists;

namespace Crateside.Demo;

/// <summary>Writes the made-up library and every store crateside keeps about it under one folder (ADR 0074).</summary>
internal sealed class DemoLibrary
{
    private readonly CratesidePaths _paths;
    private readonly DateTimeOffset _now;
    private readonly SettableTimeProvider _clock;
    private readonly List<StemTagRecord> _tags = [];
    private readonly Dictionary<string, LoudnessEntry> _loudness = new(
        StringComparer.OrdinalIgnoreCase
    );
    private readonly Dictionary<string, BeatEntry> _beats = new(StringComparer.OrdinalIgnoreCase);

    public DemoLibrary(string root, DateTimeOffset now)
    {
        _paths = new CratesidePaths { DataRoot = root };
        _now = now;
        _clock = new SettableTimeProvider(now);
    }

    public void Write()
    {
        using var index = new LiveIndex(_paths.AbletonIndex);

        foreach (var track in DemoCast.All)
        {
            if (track.HasStems)
                WriteStems(track, index);
        }

        using (var ledger = new LedgerStore(_paths, _clock))
        {
            WriteLedger(ledger);
            WriteSongRecords(ledger);
        }

        WriteIntake();
        WriteJobs();
        WriteNames();
        WriteReadings();
    }

    private DateTimeOffset StartOf(DemoTrack track) => _now - TimeSpan.FromHours(track.HoursAgo);

    private void WriteStems(DemoTrack track, LiveIndex index)
    {
        var folder = track.Stage switch
        {
            DemoStage.Library => _paths.LibraryFolder,
            DemoStage.Parked => _paths.StemsUnresolvedKey,
            _ => _paths.StagingFolder,
        };
        var landed = StartOf(track) + TimeSpan.FromMinutes(14);
        var keywords = KeywordsOf(track);
        List<TagAssignment> tags = [];

        Directory.CreateDirectory(folder);

        foreach (var stem in Enum.GetValues<DemoStem>())
        {
            var fileName = $"{track.Name} ({DemoAudio.Label(stem)}).flac";
            var path = Path.Combine(folder, fileName);
            var comment = Provenance(track, stem, landed);
            var warped = track.Stage is DemoStage.Library or DemoStage.ReadyToMove;

            // Only a file whose header is read, which the warp tempo does, needs to be audio.
            if (warped)
                DemoAudio.WriteSilence(path, track, comment);
            else
                File.WriteAllBytes(path, []);

            File.SetCreationTimeUtc(path, landed.UtcDateTime);
            File.SetLastWriteTimeUtc(path, landed.UtcDateTime);

            var file = new FileInfo(path);

            if (!warped)
                _tags.Add(
                    new StemTagRecord(
                        path,
                        file.Length,
                        file.LastWriteTimeUtc,
                        track.Title,
                        track.Artist,
                        TimeSpan.FromSeconds(track.Seconds),
                        comment
                    )
                );
            else
                WriteWarp(track, stem, file, landed);

            if (track.Stage is DemoStage.Parked)
                continue;

            var stemKeywords = StemKeywords(keywords, stem);

            tags.Add(new TagAssignment { RelativePath = fileName, Keywords = stemKeywords });
            index.Add(folder, fileName, stemKeywords);
        }

        if (tags.Count > 0)
            new FolderInfoWriter().Apply(folder, tags);
    }

    private static string Provenance(DemoTrack track, DemoStem stem, DateTimeOffset at) =>
        string.Join(
            " | ",
            "stemforge/0.9.0",
            $"preset: {DemoAudio.Label(stem)}",
            "model: demo",
            $"date: {at:yyyy-MM-dd}",
            $"source: {track.Url}",
            "format-id: 141"
        );

    private static List<string> KeywordsOf(DemoTrack track)
    {
        List<string> keywords = [];

        if (track.Genre is { } genre)
            keywords.Add(GenreVocabulary.Keyword(genre));

        if (track.Familiarity is { } familiarity)
            keywords.Add(FamiliarityVocabulary.Keyword(familiarity));

        if (TonalCenter.FromKey(track.TaggedKey) is { } center)
        {
            keywords.Add($"Key|{center.Tonic.Replace('#', '♯')}");
            keywords.Add($"Key|{(TonalCenter.IsMajor(center.Mode) ? "Major" : "Minor")}");
        }

        return keywords;
    }

    private static List<string> StemKeywords(List<string> keywords, DemoStem stem) =>
        [.. keywords, $"Type|{stem}"];

    private void WriteLedger(LedgerStore ledger)
    {
        foreach (var track in DemoCast.All.Where(track => track.HasVerdict))
            ledger.Record(
                DemoRuns.For(track, track.Verdict!, StartOf(track) + TimeSpan.FromMinutes(11))
            );
    }

    private void WriteIntake()
    {
        var intake = new IntakeStore(_paths);

        intake.Record([
            .. DemoCast.All.Select(track => new WorkItem
            {
                TrackKey = track.VideoId,
                Title = track.Title,
                ChannelTitle = $"{track.Artist} - Topic",
                AddedAt = StartOf(track),
                PremiumOutlook = PremiumOutlook.ArtTrack,
            }),
        ]);
        intake.MarkRemovalAttempted(
            DemoCast
                .All.Where(track => track.Stage is not DemoStage.Waiting)
                .Select(track => track.VideoId),
            _now
        );
    }

    private void WriteJobs()
    {
        var jobs = new JobStore(_paths, _clock);
        var notes = new StageNotes(_paths, _clock);
        var actions = new ActionLog(_paths, _clock);

        var demo = DemoJobs.Timeline(DemoCast.All, _now).ToList();

        // First in the file, so Latest activity, which reads the end of it, shows the demo tracks.
        foreach (
            var step in DemoJobs
                .History(demo, _now)
                .OrderBy(step => step.At)
                .Concat(demo.OrderBy(step => step.At))
        )
        {
            _clock.Set(step.At);

            if (step.Note is { } note)
                notes.Record(step.VideoId, note.Pass, note.Summary);
            else if (step.Action is { } action)
                actions.Record(step.VideoId, action.Kind, action.Detail);
            else
                jobs.Record(
                    step.VideoId,
                    step.Stage,
                    step.Status,
                    error: step.Error,
                    heldUntil: step.HeldUntil,
                    summary: step.Summary
                );
        }
    }

    private void WriteNames()
    {
        var names = new ResolvedNameStore(_paths);

        foreach (var track in DemoCast.All.Where(track => !track.HasVerdict))
            names.Save(
                track.VideoId,
                new ResolvedName { DisplayTitle = track.Name, BaseName = track.Name }
            );
    }

    private void WriteWarp(DemoTrack track, DemoStem stem, FileInfo file, DateTimeOffset landed)
    {
        var lufs =
            stem is DemoStem.Instrumental
                ? track.InstrumentalLufs ?? -16.5 + Spread(track, 0) * 1.8
                : track.VocalLufs ?? -21 + Spread(track, 1) * 2.2;
        var sidecar = $"{file.FullName}.asd";

        DemoAudio.Sidecar(track, stem, Math.Pow(10, (-14 - lufs) / 20), file.Length).Write(sidecar);
        File.SetLastWriteTimeUtc(sidecar, (landed + TimeSpan.FromHours(1)).UtcDateTime);

        _loudness[file.FullName] = new LoudnessEntry(
            new LoudnessStamp(file.Length, file.LastWriteTimeUtc),
            new LoudnessReading(Math.Round(lufs, 1))
        );

        if (stem is DemoStem.Instrumental && DemoAudio.Reading(track) is { } reading)
            _beats[file.FullName] = new BeatEntry(BeatStamp.Of(file), null, reading);
    }

    /// <summary>A steady figure from the track, so loudness differs from song to song without a random draw.</summary>
    private static double Spread(DemoTrack track, int salt) =>
        (track.VideoId.Sum(letter => letter) + salt) % 40 / 10.0;

    /// <summary>Seeds what the app keeps after reading audio, in place of reading it.</summary>
    private void WriteReadings()
    {
        StemTagJsonContext.Default.StemTagFile.WriteFile(
            _paths.StemTagCache,
            new StemTagFile(_tags)
        );
        LoudnessJsonContext.Default.LoudnessFile.WriteFile(
            _paths.LoudnessCache,
            new LoudnessFile(1, _loudness)
        );
        BeatJsonContext.Default.BeatFile.WriteFile(_paths.BeatCache, new BeatFile(2, _beats));
    }

    private void WriteSongRecords(LedgerStore ledger)
    {
        var genres = new GenreStore(ledger);
        var familiarity = new FamiliarityStore(ledger);
        var marks = new TrackMarkStore(ledger);

        foreach (var track in DemoCast.All)
        {
            var song = track.HasVerdict ? track.Song : track.VideoId;
            var at = StartOf(track) + TimeSpan.FromMinutes(12);

            if (track.Heard is [_, ..] heard)
                genres.Suggest(song, heard, at);
            else if (track.Genre is { } suggested)
                genres.Suggest(song, [suggested.ToLowerInvariant()], at);

            if (
                track is { Genre: { } genre, HasStems: true }
                && track.Stage is not DemoStage.Parked
            )
                genres.Settle(song, genre);

            var (fans, rank) = track switch
            {
                { ArtistFans: { } recorded, SongRank: { } place } => (recorded, place),
                { Familiarity: "Mainstream" } => (900_000L, 700_000),
                { Familiarity: "Obscure" } => (6_000L, 20_000),
                _ => (40_000L, 300_000),
            };

            familiarity.Measure(song, new FamiliarityReading(track.Artist, fans, rank), at, 1);

            if (track.Familiarity is { } settled && track.HasStems)
                familiarity.Settle(song, settled);
        }

        marks.Set(DemoCast.All[0].Song, TrackMark.ChangesKey, marked: true);
    }
}
