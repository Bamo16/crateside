using AbleKit.Analysis;
using Crateside.Components;
using Crateside.Core.Ledger;
using Crateside.Core.Warps;

namespace Crateside.Tests.Components;

public sealed class BarFormTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    public BarFormTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    /// <summary>120 BPM from beat 0 at 0 s, so a bar is 2 s, over a 200 s file.</summary>
    private WarpGroup Warped()
    {
        var path = Path.Combine(_root, $"{Guid.NewGuid():n} (Instrumental - Full).flac");

        File.WriteAllBytes(path, [1, 2, 3]);
        new AnalysisFile
        {
            Audio = AudioAnalysis.FromSamples(
                [.. Enumerable.Repeat(0.5f, 44_100 * 2)],
                2,
                [],
                3
            ) with
            {
                HeadTable = [200 * 44_100],
            },
            Warp = new Warp
            {
                Markers = [new(0, 0), new(120, 240), new(120.015625, 240.03125)],
                TimeSignature = new(4, 4),
            },
            Clip = new Clip
            {
                LoopStart = 4,
                LoopEnd = 8,
                LoopOn = false,
            },
            IsDefaultClipSaved = true,
        }.Write($"{path}.asd");

        return WarpGroup.Of([
            new StemWarpReader(new WrittenWarps(), new TransientOrigins(), new LiveAnalyses()).Read(
                path
            ),
        ]);
    }

    private static BarForm Form(params string[] ranges)
    {
        BarForm form = new(inBars: true);

        form.Lines.Clear();
        form.Lines.AddRange(ranges.Select(range => new BarLine { Range = range }));

        return form;
    }

    [Fact]
    public void Ranges_that_meet_on_a_bar_do_not_overlap_there()
    {
        var group = Warped();

        var windows = Form("1-26", "26-end").Windows(group);

        Assert.Equal(2, windows.Count);
        Assert.Equal((0, 50), (windows[0].Start, windows[0].End));
        Assert.Equal(new BarSpan(1, 25), windows[0].Bars);
        Assert.Equal(50, windows[1].Start);
        Assert.Equal(200, windows[1].End, 0.01);
    }

    [Fact]
    public void A_row_shows_the_stretch_it_resolves_to()
    {
        var form = Form("26-end", "start-10", "999-1000");
        var group = Warped();

        Assert.Equal("bar 26 to the end (0:50–3:20)", form.Stretch(form.Lines[0], group));
        Assert.Equal("the start to bar 10 (0:00–0:20)", form.Stretch(form.Lines[1], group));
        Assert.Null(form.Stretch(form.Lines[2], group));
    }
}
