using AbleKit.Analysis;
using Crateside.Core;
using Crateside.Core.SourceOffsets;
using Crateside.Core.Warps;
using Microsoft.Extensions.Time.Testing;

namespace Crateside.Tests.SourceOffsets;

/// <summary>ADR 0111's saves on copies of Bamo's own files, never on the originals.</summary>
public sealed class AddedSourcesRealLibraryTests : IDisposable
{
    private const string Staging = @"P:\RYAN\Ableton\Sample Staging";
    private const string Mashups = @"P:\RYAN\Ableton\Mashup Samples";
    private const int Rate = 44_100;

    private static readonly CratesidePaths RealPaths = Tests.RealPaths.Create();

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    private readonly FakeTimeProvider _time = new(new(2026, 11, 20, 9, 0, 0, TimeSpan.Zero));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public Task Both_official_cuts_of_Pop_Lock_and_Drop_It_are_shifted_by_their_measured_offsets() =>
        ShiftedAsync(
            Staging,
            "Huey - Pop, Lock & Drop It (Main Version) (Instrumental - Full).flac",
            [
                ("Huey - Pop, Lock & Drop It (Main Version) (Official Instrumental).flac", 19_470),
                ("Huey - Pop, Lock & Drop It (Main Version) (Official Acappella).flac", 359_546),
            ]
        );

    [Fact]
    public Task The_Gucci_Gucci_acapella_is_shifted_by_its_measured_offset() =>
        ShiftedAsync(
            Mashups,
            "Kreayshawn - Gucci Gucci (Instrumental - Full).flac",
            [("Kreayshawn - Gucci Gucci (Acapella).flac", -2_812)]
        );

    private async Task ShiftedAsync(
        string folder,
        string reference,
        (string Name, int Offset)[] added
    )
    {
        var source = Path.Combine(folder, reference);

        Assert.SkipUnless(File.Exists(source), "The library is not here.");

        var realWritten = new WrittenWarps(RealPaths.WrittenWarpsFile);
        var written = new WrittenWarps();
        var saved = new SavedWarps();
        var bytes = new Dictionary<string, byte[]>();

        Directory.CreateDirectory(_root);

        foreach (var file in Directory.EnumerateFiles(folder, $"{StemOf(reference)}*"))
            Copy(file);

        var offsets = new SourceOffsetStore();
        var files = new List<AddedSourceFile>();
        var mix = Path.Combine(_root, reference);

        foreach (var (name, offset) in added)
        {
            var copy = Path.Combine(_root, name);

            if (!File.Exists(copy))
                Copy(Path.Combine(folder, name));

            if (realWritten.IsCratesides(Path.Combine(folder, name)))
                written.RecordStarter(copy);

            bytes[copy] = File.ReadAllBytes($"{copy}.asd");
            files.Add(new AddedSourceFile(copy, name, name));
            offsets.Save(
                name,
                new SourceOffsetRecord(
                    "main",
                    FileStamp.Of(mix)!,
                    FileStamp.Of(copy)!,
                    new OffsetReading
                    {
                        Verdict = OffsetVerdict.Aligned,
                        SampleRate = Rate,
                        WindowSeconds = 5,
                        Offset = offset,
                    },
                    _time.GetUtcNow()
                )
            );
        }

        var backups = new SidecarBackups(Path.Combine(_root, "backups"), _time);
        var writer = new SidecarWriter(
            new FfmpegAudioDecoder(),
            new OnsetDetector(),
            written,
            new TransientOrigins()
        );
        var editor = new WarpEditor(
            written,
            saved,
            backups,
            new WarpActions(writer, written, new LiveAnalyses(), backups, _time),
            _time,
            offsets
        );
        var siblings = WarpSiblings.Scan([_root]);
        var plan = editor.Plan(siblings, WarpSiblings.Read(mix)!, files);

        Assert.SkipUnless(plan is WarpSavePlan.Ready, "The reference holds no saved warp.");

        var ready = (WarpSavePlan.Ready)plan;

        Assert.All(ready.Added, item => Assert.Null(item.Skip));

        var outcome = await editor.SaveAsync(
            ready,
            siblings,
            new WarpDraft().Nudged(1),
            ct: TestContext.Current.CancellationToken
        );
        var save = Assert.IsType<WarpSaveOutcome.Saved>(outcome).Save;

        Assert.Equal(added.Length, save.Shifted.Count);

        foreach (var (name, offset) in added)
        {
            var copy = Path.Combine(_root, name);
            var warp = AnalysisFile.Read($"{copy}.asd").Warp;
            var mixWarp = AnalysisFile.Read($"{mix}.asd").Warp;

            Assert.NotEqual(bytes[copy], File.ReadAllBytes($"{copy}.asd"));
            Assert.Contains(
                save.Shifted,
                stem =>
                    stem.Path == copy && File.ReadAllBytes(stem.Backup).SequenceEqual(bytes[copy])
            );
            Assert.Equal(
                mixWarp.SecondsAt(64)!.Value - ((double)offset / Rate),
                warp.SecondsAt(64)!.Value,
                1e-4
            );
        }

        editor.Undo(save);

        Assert.All(
            added,
            one =>
                Assert.Equal(
                    bytes[Path.Combine(_root, one.Name)],
                    File.ReadAllBytes($"{Path.Combine(_root, one.Name)}.asd")
                )
        );
    }

    private static string StemOf(string reference) => reference[..reference.IndexOf(" (")];

    private void Copy(string file)
    {
        if (File.Exists(file))
            File.Copy(file, Path.Combine(_root, Path.GetFileName(file)), overwrite: true);
    }
}
