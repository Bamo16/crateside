using Crateside.Core.Staging;

namespace Crateside.Tests.Staging;

public class AbletonFolderInfoReaderTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);

        GC.SuppressFinalize(this);
    }

    private AbletonFolderInfo ReadXmp(string itemsXml)
    {
        var folderInfo = Path.Combine(_root, "Ableton Folder Info");
        Directory.CreateDirectory(folderInfo);

        File.WriteAllText(
            Path.Combine(folderInfo, "dc66a3fa-0fe1-5352-91cf-3ec237e9ee90.xmp"),
            $"""
            <x:xmpmeta xmlns:x="adobe:ns:meta/" x:xmptk="XMP Core 6.0.0">
               <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">
                  <rdf:Description rdf:about=""
                        xmlns:dc="http://purl.org/dc/elements/1.1/"
                        xmlns:ablFR="https://ns.ableton.com/xmp/fs-resources/1.0/">
                     <dc:format>application/vnd.ableton.folder</dc:format>
                     <ablFR:resource>folder</ablFR:resource>
                     <ablFR:platform>win</ablFR:platform>
                     <ablFR:items>
                        <rdf:Bag>
            {itemsXml}
                        </rdf:Bag>
                     </ablFR:items>
                  </rdf:Description>
               </rdf:RDF>
            </x:xmpmeta>
            """
        );

        return new AbletonFolderInfoReader().Read(_root);
    }

    private static string Entry(string filePath, params string[] keywords) =>
        $"""
                         <rdf:li rdf:parseType="Resource">
                            <ablFR:filePath>{filePath}</ablFR:filePath>
                            <ablFR:keywords>
                               <rdf:Bag>
            {string.Join("\n", keywords.Select(k => $"                     <rdf:li>{k}</rdf:li>"))}
                               </rdf:Bag>
                            </ablFR:keywords>
                         </rdf:li>
            """;

    [Fact]
    public void Recombines_the_two_key_keywords_into_the_ledger_spelling()
    {
        var info = ReadXmp(
            Entry(
                "Jackson 5 - I Want You Back (Instrumental - Full).flac",
                "Key|G♯",
                "Key|Major",
                "Type|Instrumental"
            )
        );

        Assert.True(
            info.TryGet("Jackson 5 - I Want You Back (Instrumental - Full).flac", out var tags)
        );
        Assert.Equal("G# major", tags.Key); // Unicode ♯ normalized, mode lowercased
        Assert.Equal("Instrumental", tags.Type);
        Assert.True(tags.IsKeyTagged);
        Assert.False(tags.HasPartialKey);
    }

    [Fact]
    public void A_tonic_with_no_mode_is_not_a_key()
    {
        // Real data: two Fedde Le Grand stems carry Key|D and no Major/Minor keyword.
        var info = ReadXmp(
            Entry(
                "Fedde Le Grand - Put Your Hands Up For Detroit.flac",
                "Key|D",
                "Type|Instrumental"
            )
        );

        Assert.True(
            info.TryGet("Fedde Le Grand - Put Your Hands Up For Detroit.flac", out var tags)
        );
        Assert.Null(tags.Key);
        Assert.False(tags.IsKeyTagged);
        Assert.True(tags.HasPartialKey);
    }

    [Fact]
    public void An_entry_with_keywords_but_no_key_is_not_key_tagged()
    {
        // "Tagged" means "has a Key| keyword", not "has an entry" — an entry exists for any
        // file with any keyword at all.
        var info = ReadXmp(Entry("Loop.flac", "Type|Loop"));

        Assert.True(info.TryGet("Loop.flac", out var tags));
        Assert.Null(tags.Key);
        Assert.False(tags.HasPartialKey);
        Assert.Equal("Loop", tags.Type);
    }

    [Fact]
    public void Resolves_by_absolute_path_under_the_root()
    {
        var info = ReadXmp(
            Entry("Yes - Owner of a Lonely Heart (Drums).flac", "Key|C", "Key|Minor")
        );

        Assert.True(
            info.TryGet(
                Path.Combine(_root, "Yes - Owner of a Lonely Heart (Drums).flac"),
                out var tags
            )
        );
        Assert.Equal("C minor", tags.Key);
    }

    [Fact]
    public void A_file_with_no_entry_is_simply_absent()
    {
        var info = ReadXmp(Entry("Tagged.flac", "Key|A", "Key|Minor"));

        Assert.False(info.TryGet("Never Tagged.flac", out _));
    }

    [Fact]
    public void A_root_with_no_folder_info_reads_empty_rather_than_throwing()
    {
        Directory.CreateDirectory(_root);

        var info = new AbletonFolderInfoReader().Read(_root);

        Assert.Empty(info.Entries);
        Assert.False(info.TryGet("anything.flac", out _));
    }

    [Fact]
    public void A_malformed_xmp_reads_empty_rather_than_throwing()
    {
        var folderInfo = Path.Combine(_root, "Ableton Folder Info");
        Directory.CreateDirectory(folderInfo);
        File.WriteAllText(Path.Combine(folderInfo, "half-written.xmp"), "<x:xmpmeta><rdf:RDF");

        Assert.Empty(new AbletonFolderInfoReader().Read(_root).Entries);
    }

    [Fact]
    public void Reads_while_the_file_is_held_open_for_writing()
    {
        // Ableton owns this file while it runs; the board must still read it.
        var info = ReadXmp(Entry("Held.flac", "Key|F", "Key|Major"));
        var xmp = Directory.GetFiles(Path.Combine(_root, "Ableton Folder Info"), "*.xmp")[0];

        using var holder = new FileStream(
            xmp,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.ReadWrite
        );

        Assert.Single(new AbletonFolderInfoReader().Read(_root).Entries);
    }
}
