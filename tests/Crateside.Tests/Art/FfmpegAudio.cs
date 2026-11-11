using System.ComponentModel;
using System.Text.RegularExpressions;
using Crateside.Core.Orchestration.Loop;

namespace Crateside.Tests.Art;

/// <summary>Real audio files for tests, made and probed with the ffmpeg on PATH.</summary>
internal static partial class FfmpegAudio
{
    private static readonly Lazy<bool> Present = new(() =>
        Run(["-version"]).Result?.Succeeded is true
    );

    [GeneratedRegex(@"Video:.*?, (?<Width>\d+)x(?<Height>\d+)", RegexOptions.ExplicitCapture)]
    private static partial Regex Size { get; }

    public static bool IsAvailable => Present.Value;

    /// <summary>A one-second FLAC; carries a <paramref name="color"/> picture of the given size when width is given.</summary>
    public static async Task MakeAsync(
        string path,
        int width = 0,
        int height = 0,
        string color = "red"
    )
    {
        var picture = $"{path}.png";

        if (width > 0)
            await Run([
                "-v",
                "error",
                "-y",
                "-f",
                "lavfi",
                "-i",
                $"color=c={color}:s={width}x{height}:d=1,format=rgb24",
                "-frames:v",
                "1",
                picture,
            ]);

        string[] arguments =
            width > 0
                ?
                [
                    "-v",
                    "error",
                    "-y",
                    "-f",
                    "lavfi",
                    "-i",
                    "sine=d=1",
                    "-i",
                    picture,
                    "-map",
                    "0",
                    "-map",
                    "1",
                    "-c:a",
                    "flac",
                    "-c:v",
                    "mjpeg",
                    "-disposition:v",
                    "attached_pic",
                    path,
                ]
                : ["-v", "error", "-y", "-f", "lavfi", "-i", "sine=d=1", "-c:a", "flac", path];

        var outcome = await Run(arguments);

        File.Delete(picture);
        Assert.True(outcome?.Succeeded, outcome?.StandardError);
    }

    /// <summary>The width and height of the picture in <paramref name="path"/>.</summary>
    public static async Task<(int Width, int Height)> SizeOfAsync(string path)
    {
        var outcome = await Run(["-hide_banner", "-i", path]);
        var match = Size.Match(outcome!.StandardError).Groups;

        return (int.Parse(match["Width"].Value), int.Parse(match["Height"].Value));
    }

    private static async Task<ProcessOutcome?> Run(string[] arguments)
    {
        try
        {
            return await new ProcessRunner().RunAsync(new ProcessRequest("ffmpeg", arguments));
        }
        catch (Win32Exception)
        {
            return null;
        }
    }
}

/// <summary>A runner that counts what it runs.</summary>
internal sealed class CountingRunner : IProcessRunner
{
    private readonly ProcessRunner _inner = new();

    public int Runs { get; private set; }

    public Task<ProcessOutcome> RunAsync(ProcessRequest request, CancellationToken ct = default)
    {
        Runs++;

        return _inner.RunAsync(request, ct);
    }
}
