using System.Buffers.Binary;
using System.Diagnostics;

namespace Crateside.Core.Warps;

/// <summary>An audio file decoded to floats from −1 to 1, channels interleaved, at its own rate.</summary>
public sealed record DecodedAudio(float[] Samples, int ChannelCount, int SampleRate)
{
    public int Frames => Samples.Length / ChannelCount;
}

public interface IAudioDecoder
{
    /// <summary>Null when the file cannot be decoded.</summary>
    Task<DecodedAudio?> DecodeAsync(string path, CancellationToken ct = default);
}

/// <summary>Decodes with ffmpeg to 32-bit float WAV on stdout, keeping the file's channels and rate.</summary>
public sealed class FfmpegAudioDecoder(string fileName = "ffmpeg") : IAudioDecoder
{
    private const int FloatFormat = 3;
    private const int ExtensibleFormat = 0xFFFE;

    public async Task<DecodedAudio?> DecodeAsync(string path, CancellationToken ct = default)
    {
        var info = new ProcessStartInfo(
            fileName,
            ["-v", "error", "-i", path, "-map", "0:a:0", "-f", "wav", "-acodec", "pcm_f32le", "-"]
        )
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(info);

        if (process is null)
            return null;

        var stderr = process.StandardError.ReadToEndAsync(ct);
        using var wav = new MemoryStream();

        try
        {
            await process.StandardOutput.BaseStream.CopyToAsync(wav, ct);
            await process.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);

            throw;
        }

        await stderr;

        return process.ExitCode is 0 ? Parse(wav.GetBuffer().AsSpan(0, (int)wav.Length)) : null;
    }

    /// <summary>
    /// Reads a float WAV whose size fields may be unset, as ffmpeg writes to a pipe: the data runs
    /// to the end.
    /// </summary>
    internal static DecodedAudio? Parse(ReadOnlySpan<byte> wav)
    {
        if (
            wav.Length < 12
            || !wav[..4].SequenceEqual("RIFF"u8)
            || !wav[8..12].SequenceEqual("WAVE"u8)
        )
            return null;

        var p = 12;
        int? channels = null;
        int? rate = null;

        while (p + 8 <= wav.Length)
        {
            var id = wav.Slice(p, 4);
            var size = BinaryPrimitives.ReadUInt32LittleEndian(wav[(p + 4)..]);
            var body = p + 8;

            if (id.SequenceEqual("fmt "u8))
            {
                if (body + 16 > wav.Length)
                    return null;

                var format = BinaryPrimitives.ReadUInt16LittleEndian(wav[body..]);
                var bits = BinaryPrimitives.ReadUInt16LittleEndian(wav[(body + 14)..]);

                if (format is not (FloatFormat or ExtensibleFormat) || bits is not 32)
                    return null;

                channels = BinaryPrimitives.ReadUInt16LittleEndian(wav[(body + 2)..]);
                rate = BinaryPrimitives.ReadInt32LittleEndian(wav[(body + 4)..]);
                p = body + (int)size + (int)(size & 1);

                continue;
            }

            if (id.SequenceEqual("data"u8))
            {
                if (channels is not > 0 || rate is not > 0)
                    return null;

                var data = wav[body..];
                var frames = data.Length / sizeof(float) / channels.Value;

                if (frames is 0)
                    return null;

                var samples = new float[frames * channels.Value];
                data[..(samples.Length * sizeof(float))]
                    .CopyTo(System.Runtime.InteropServices.MemoryMarshal.AsBytes(samples.AsSpan()));

                return new DecodedAudio(samples, channels.Value, rate.Value);
            }

            p = body + (int)size + (int)(size & 1);
        }

        return null;
    }
}
