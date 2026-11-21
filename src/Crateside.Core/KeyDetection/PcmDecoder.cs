using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using Crateside.Core.Ledger;
using Crateside.Core.Orchestration.Loop;

namespace Crateside.Core.KeyDetection;

/// <summary>Decodes audio, or one stretch of it, to 44.1 kHz mono float samples through ffmpeg.</summary>
public sealed class PcmDecoder(IProcessRunner runner, string ffmpeg)
{
    public const int SampleRate = 44100;

    public async Task<DecodeOutcome> DecodeAsync(
        string audioPath,
        RunWindow? window,
        CancellationToken ct = default
    )
    {
        var output = Path.Combine(Path.GetTempPath(), $"crateside-pcm-{Guid.NewGuid():N}.f32");

        try
        {
            var outcome = await runner.RunAsync(
                new ProcessRequest(
                    ffmpeg,
                    [
                        "-nostdin",
                        "-hide_banner",
                        "-v",
                        "error",
                        .. WindowArguments(window),
                        "-i",
                        audioPath,
                        "-vn",
                        "-ac",
                        "1",
                        "-ar",
                        SampleRate.ToString(CultureInfo.InvariantCulture),
                        "-f",
                        "f32le",
                        "-y",
                        output,
                    ]
                ),
                ct
            );

            if (!outcome.Succeeded)
                return new DecodeOutcome.Failed(
                    outcome.LastLine ?? $"ffmpeg exited with {outcome.ExitCode} decoding the audio"
                );

            var bytes = await File.ReadAllBytesAsync(output, ct);

            return bytes.Length < sizeof(float)
                ? new DecodeOutcome.Failed("ffmpeg decoded no audio")
                : new DecodeOutcome.Decoded(MemoryMarshal.Cast<byte, float>(bytes).ToArray());
        }
        catch (Win32Exception ex)
        {
            return new DecodeOutcome.Failed($"ffmpeg could not be started: {ex.Message}");
        }
        finally
        {
            File.Delete(output);
        }
    }

    /// <summary>Seeks before the input, so only the window is decoded.</summary>
    internal static IEnumerable<string> WindowArguments(RunWindow? window) =>
        window is { Start: var start, End: var end }
            ?
            [
                "-ss",
                start.ToString("0.###", CultureInfo.InvariantCulture),
                "-t",
                (end - start).ToString("0.###", CultureInfo.InvariantCulture),
            ]
            : [];
}

public abstract record DecodeOutcome
{
    private DecodeOutcome() { }

    public sealed record Decoded(float[] Samples) : DecodeOutcome;

    public sealed record Failed(string Error) : DecodeOutcome;
}
