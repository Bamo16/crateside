using AbleKit.Tags;
using Crateside.Core;
using Crateside.Core.Imports;
using Crateside.Core.Naming;
using Crateside.Core.Orchestration.Edits;
using Crateside.Core.Staging;
using Crateside.Tests.Orchestration.Edits;

namespace Crateside.Tests.Imports;

public sealed class ImportNameRepairTests : IDisposable
{
    private const string Key = "file~96fd918210394746";

    private readonly IntakeWorld _world = new();
    private readonly List<StagedFile> _staged = [];
    private readonly CratesidePaths _paths;

    public ImportNameRepairTests()
    {
        _paths = new CratesidePaths { StagingFolder = Path.Combine(_world.Root, "Staging") };
        Directory.CreateDirectory(_paths.StagingFolder);
        _world.Names.Save(
            Key,
            new ResolvedName
            {
                DisplayTitle = "Yoko Shimomura - Fight Against Koopa",
                BaseName = "1-05 Fight Against Koopa",
            }
        );
    }

    public void Dispose() => _world.Dispose();

    private ImportNameRepair Repair() =>
        new(
            _world.Names,
            _world.Instructions,
            new TrackRename(
                _world.Instructions,
                new ListedStagedFiles(() => _staged),
                new FolderInfoWriter(),
                _paths
            )
        );

    private string Stage(string name)
    {
        var path = Path.Combine(_paths.StagingFolder, name);

        File.WriteAllText(path, "stem");
        _staged.Add(new StagedFile { Path = path, FileName = name });

        return path;
    }

    [Fact]
    public void Files_staged_under_the_download_name_get_the_tag_name_through_the_rename()
    {
        var stem = Stage("1-05 Fight Against Koopa (Drums).flac");

        var (renamed, refused) = Repair().Run();

        Assert.Equal([Key], renamed);
        Assert.Empty(refused);
        Assert.False(File.Exists(stem));
        Assert.True(
            File.Exists(
                Path.Combine(
                    _paths.StagingFolder,
                    "Yoko Shimomura - Fight Against Koopa (Drums).flac"
                )
            )
        );
        Assert.Null(
            _world.Instructions.Read().FirstOrDefault(track => track.TrackKey == Key)?.Name
        );
    }

    [Fact]
    public void A_track_the_person_renamed_is_left_alone()
    {
        var stem = Stage("Mine (Drums).flac");

        _world.Instructions.SetName(Key, "Mine");

        Assert.Empty(Repair().Run().Renamed);
        Assert.True(File.Exists(stem));
    }

    [Fact]
    public void Files_already_under_the_tag_name_are_not_touched_again()
    {
        Stage("Yoko Shimomura - Fight Against Koopa (Drums).flac");

        Assert.Empty(Repair().Run().Renamed);
    }
}
