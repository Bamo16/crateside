using AbleKit.Analysis;

namespace Crateside.Tests.Warps;

/// <summary>
/// The bytes of a saved clip with Warp off and no warp markers, the shape that crashes Live (ADR 0091).
/// AbleKit refuses to write it, so they are an unsaved fixture with its two flags flipped.
/// </summary>
internal static class CrashingSidecar
{
    public static byte[] Bytes()
    {
        var unsaved = AnalysisFile.Read(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "sidecar-clicks-unsaved.asd")
        );
        var bytes = unsaved.ToBytes();

        var saved = FirstDifference(
            bytes,
            (unsaved with { IsDefaultClipSaved = true, Warp = Warp.Off(0) }).ToBytes()
        );
        var warpOff = FirstDifference(
            bytes,
            (unsaved with { Warp = unsaved.Warp with { IsWarped = false } }).ToBytes()
        );

        bytes[saved] = 1;
        bytes[warpOff] = 0;

        var shape = AnalysisFile.Parse(bytes);

        Assert.True(shape is { IsDefaultClipSaved: true, Warp: { IsWarped: false, Markers: [] } });

        return bytes;
    }

    private static int FirstDifference(byte[] left, byte[] right)
    {
        for (var i = 0; i < left.Length; i++)
        {
            if (left[i] != right[i])
                return i;
        }

        throw new InvalidOperationException("the two files are identical");
    }
}
