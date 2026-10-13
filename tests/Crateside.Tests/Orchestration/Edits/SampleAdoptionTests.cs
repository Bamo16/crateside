using Crateside.Core;
using Crateside.Core.Orchestration.Edits;
using Crateside.Core.Plans;
using Crateside.Core.Playlist;
using Crateside.Core.Samples;
using Crateside.Core.Tracks;
using Crateside.Core.WorkLists;
using Microsoft.Extensions.Options;

namespace Crateside.Tests.Orchestration.Edits;

public sealed class SampleAdoptionTests : IDisposable
{
    private const string Track = "aaaaaaaaaaa";

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    private readonly TrackInstructionStore _settings;
    private readonly CratesidePaths _paths;

    public SampleAdoptionTests()
    {
        _paths = new CratesidePaths
        {
            StagingFolder = Path.Combine(_root, "Staging"),
            LibraryFolder = Path.Combine(_root, "Library"),
        };
        Directory.CreateDirectory(_paths.StagingFolder);
        Directory.CreateDirectory(_paths.LibraryFolder);

        _settings = new TrackInstructionStore(
            new QueueFile(Options.Create(new PlaylistOptions()), Path.Combine(_root, "queue.json"))
        );
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);

        GC.SuppressFinalize(this);
    }

    private SampleAdoption Adoption() => new(_settings, _paths);

    private static TrackRowFacts Facts(StemPlan? plan = null) =>
        new()
        {
            VideoId = Track,
            Plan = plan ?? StemPlan.Default,
            Uploads = [Track],
        };

    private string HandBuilt(string name, string? comment = null)
    {
        var path = Path.Combine(_paths.LibraryFolder, name);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "silence.flac"), path);

        using var file = TagLib.File.Create(path);
        file.Tag.Comment = comment;
        file.Save();

        return path;
    }

    [Fact]
    public void Only_files_nothing_can_place_are_offered()
    {
        var bare = HandBuilt("Song (Instrumental - Mixed).flac");
        HandBuilt(
            "Song (Vocal - Full).flac",
            $"stemforge/0.3.1.0 | preset: Vocal - Full | source: https://www.youtube.com/watch?v={Track}"
        );

        Assert.Equal([bare], Adoption().Candidates());
    }

    [Fact]
    public void Adopting_writes_the_provenance_in_place_and_names_it_on_the_plan()
    {
        var path = HandBuilt("Song (Instrumental - Mixed).flac");

        Assert.Null(Adoption().Apply(Facts(), path, SampleType.Instrumental));

        var provenance = StemTagReader.Read(path)?.Provenance;
        Assert.True(provenance?.IsAdopted);
        Assert.Equal("Instrumental - Mixed", provenance?.Label);
        Assert.Equal(SampleType.Instrumental, provenance?.Type);
        Assert.Equal(Track, provenance?.SourceVideoId);

        var plan = _settings.Read().Single(t => t.VideoId == Track).Plan;
        Assert.Equal("Instrumental - Mixed", Assert.Single(plan!.AdoptedSamples).Label);
    }

    [Fact]
    public void A_file_with_no_label_in_its_name_is_refused()
    {
        var path = HandBuilt("Ludwig Göransson - The Mandalorian.flac");

        Assert.False(Adoption().PlanFor(Facts(), path, SampleType.Instrumental).IsPossible);
    }

    [Fact]
    public void Nothing_is_adopted_until_the_user_says_what_it_is()
    {
        var path = HandBuilt("Song (Instrumental - Mixed).flac");

        Assert.NotNull(Adoption().Apply(Facts(), path, type: null));
        Assert.Null(StemTagReader.Read(path)?.Provenance);
    }

    [Fact]
    public void A_label_the_track_already_uses_is_refused()
    {
        var path = HandBuilt("Song (Instrumental - Mixed).flac");
        var plan = StemPlan.Default.Keeping(new AdoptedSample { Label = "Instrumental - Mixed" });

        Assert.False(Adoption().PlanFor(Facts(plan), path, SampleType.Instrumental).IsPossible);
    }
}
