using Crateside.Core;
using Crateside.Core.Imports;
using Crateside.Core.Ledger;
using Crateside.Core.Naming;
using Crateside.Core.Playlist;
using Crateside.Core.WorkLists;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using TagLib;
using TagLib.Ogg;

namespace Crateside.Tests.Imports;

/// <summary>Every store intake touches, in one temporary folder.</summary>
internal sealed class IntakeWorld : IDisposable
{
    public IntakeWorld()
    {
        Root = Path.Combine(Path.GetTempPath(), "crateside-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(Root);

        Paths = new CratesidePaths
        {
            ImportsFolder = Path.Combine(Root, "imports"),
            CoversFolder = Path.Combine(Root, "covers"),
            WorkingFolder = Path.Combine(Root, "working"),
            Inbox = Path.Combine(Root, "inbox"),
            RecipeFile = Path.Combine(Root, "recipe.json"),
            ResolvedNamesFile = Path.Combine(Root, "names.json"),
        };

        Time = new FakeTimeProvider(new DateTimeOffset(2026, 11, 4, 12, 0, 0, TimeSpan.Zero));
        Imports = new ImportStore(Paths);
        Ledger = new LedgerStore(LedgerStore.InMemory, Time);
        Intake = new IntakeStore(Ledger);
        Names = new ResolvedNameStore(Paths);
        Instructions = new TrackInstructionStore(Ledger);
        Recipes = new RecipeStore(Paths);
        Files = new TrackIntake(Imports, Intake, Names, Instructions, Recipes, Paths, Time);
    }

    public string Root { get; }

    public CratesidePaths Paths { get; }

    public FakeTimeProvider Time { get; }

    public LedgerStore Ledger { get; }

    public ImportStore Imports { get; }

    public IntakeStore Intake { get; }

    public ResolvedNameStore Names { get; }

    public TrackInstructionStore Instructions { get; }

    public RecipeStore Recipes { get; }

    public TrackIntake Files { get; }

    public void Dispose()
    {
        Ledger.Dispose();

        if (Directory.Exists(Root))
            Directory.Delete(Root, recursive: true);
    }

    /// <summary>A FLAC tagged as a gamerip is: artist, title, album, date text, composer, organization, cover.</summary>
    public static string TaggedFlac(
        string folder,
        string name,
        string title = "GRADUATION",
        string seed = "a"
    )
    {
        Directory.CreateDirectory(folder);

        var path = Path.Combine(folder, name);

        System.IO.File.Copy(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "silence.flac"),
            path,
            overwrite: true
        );

        using var file = TagLib.File.Create(path);

        file.Tag.Performers = ["Be For U"];
        file.Tag.Title = title;
        file.Tag.Album = "DDR EXTREME";
        file.Tag.AlbumArtists = ["Konami"];
        file.Tag.Composers = ["Naoki Maeda"];
        file.Tag.Genres = ["Game Soundtrack"];
        file.Tag.Track = 1;
        file.Tag.TrackCount = 37;
        file.Tag.Disc = 1;
        file.Tag.DiscCount = 2;
        file.Tag.Comment = $"ripped {seed}";
        ((XiphComment)file.GetTag(TagTypes.Xiph, true)).SetField("DATE", "March 9, 2003");
        ((XiphComment)file.GetTag(TagTypes.Xiph, true)).SetField("ORGANIZATION", "Konami, Rare");
        ((XiphComment)file.GetTag(TagTypes.Xiph, true)).SetField("OST", "KMCA-123");
        file.Tag.Pictures =
        [
            new Picture(new ByteVector([1, 2, 3, 4]))
            {
                MimeType = "image/png",
                Type = PictureType.FrontCover,
            },
        ];
        file.Save();

        return path;
    }
}
