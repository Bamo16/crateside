using Crateside.Core;
using Crateside.Core.Imports;
using Crateside.Core.Orchestration.Handoff;
using Crateside.Tests.Imports;

namespace Crateside.Tests.Orchestration.Handoff;

/// <summary>An imported track's stems reach Staging under the name its tags gave it (ADR 0079).</summary>
public sealed class ImportedTrackNameTests : IDisposable
{
    private readonly IntakeWorld _world = new();

    public void Dispose() => _world.Dispose();

    private (string Key, string Name) Import() =>
        _world.Files.TakeInFile(
            _world.TaggedFlac(Path.Combine(_world.Root, "in"), "1-05 Fight Against Koopa.flac"),
            IntakeMode.Copy
        ) switch
        {
            IntakeOutcome.TakenIn(var key, var name) => (key, name),
            _ => throw new InvalidOperationException("not taken in"),
        };

    private string? NameForHandoff(string key) =>
        HandoffDrainer.NameFor(
            key,
            _world.Instructions.Read().FirstOrDefault(track => track.TrackKey == key),
            _world.Names.Read()
        );

    private string Handoff(string key, string? name)
    {
        var cut = Path.Combine(_world.Root, "cut");
        var staging = Path.Combine(_world.Root, "Staging");
        var stem = Path.Combine(cut, "1-05 Fight Against Koopa (Instrumental - Full).flac");

        Directory.CreateDirectory(cut);
        File.WriteAllText(stem, key);

        Assert.True(
            new HandoffRunner(new CratesidePaths { StagingFolder = staging })
                .Run([stem], name)
                .IsSuccess
        );

        return Path.GetFileName(Directory.EnumerateFiles(staging).Single());
    }

    [Fact]
    public void An_import_cut_from_a_file_named_by_its_track_number_is_staged_under_artist_and_title()
    {
        var (key, name) = Import();

        Assert.Equal("Be For U - GRADUATION", name);
        Assert.Equal(
            "Be For U - GRADUATION (Instrumental - Full).flac",
            Handoff(key, NameForHandoff(key))
        );
    }

    [Fact]
    public void A_name_the_person_gave_wins_over_the_tags()
    {
        var (key, _) = Import();

        _world.Instructions.SetName(key, "My Own Name");

        Assert.Equal("My Own Name (Instrumental - Full).flac", Handoff(key, NameForHandoff(key)));
    }

    [Fact]
    public void A_character_a_file_name_cannot_hold_is_replaced_not_dropped()
    {
        var (key, _) = Import();

        _world.Names.Save(key, new() { DisplayTitle = "AC/DC - Who Made Who", BaseName = "x" });

        Assert.Equal("AC_DC - Who Made Who", NameForHandoff(key));
    }

    [Fact]
    public void A_youtube_track_is_left_to_the_name_its_download_already_has()
    {
        _world.Names.Save("aaaaaaaaaaa", new() { DisplayTitle = "Shown", BaseName = "Downloaded" });
        _world.Instructions.SetPlan("aaaaaaaaaaa", Recipes.PlanOf(Recipe.Instrumental));

        Assert.Null(NameForHandoff("aaaaaaaaaaa"));
    }
}
