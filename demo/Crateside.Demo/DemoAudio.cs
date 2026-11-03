using System.Diagnostics;
using AbleKit.Analysis;

namespace Crateside.Demo;

internal enum DemoStem
{
    Vocal,
    Instrumental,
}

/// <summary>The stand-ins for audio: a decodable file of silence where the app must read a header, and a made-up waveform for the sidecar's overview.</summary>
internal static class DemoAudio
{
    public const int SampleRate = 22_050;

    /// <summary>Silence before the first downbeat, so a warp has something to find.</summary>
    public const double Lead = 0.25;

    public static string Label(DemoStem stem) =>
        stem is DemoStem.Vocal ? "Vocal - Full" : "Instrumental - Full";

    public static int Bars(DemoTrack track) => (int)((track.Seconds - Lead) * track.Bpm / 240);

    /// <summary>One second of silence at <see cref="SampleRate"/>: TagLib reads its sample rate and tags, and nothing decodes the rest.</summary>
    public static void WriteSilence(string path, DemoTrack track, string comment)
    {
        using var ffmpeg = Process.Start(
            new ProcessStartInfo("ffmpeg")
            {
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                ArgumentList =
                {
                    "-y",
                    "-loglevel",
                    "error",
                    "-f",
                    "lavfi",
                    "-i",
                    $"anullsrc=r={SampleRate}:cl=mono",
                    "-t",
                    "1",
                    "-metadata",
                    $"title={track.Title}",
                    "-metadata",
                    $"artist={track.Artist}",
                    "-metadata",
                    $"comment={comment}",
                    "-c:a",
                    "flac",
                    path,
                },
            }
        )!;
        var error = ffmpeg.StandardError.ReadToEnd();

        ffmpeg.WaitForExit();

        if (ffmpeg.ExitCode is not 0)
            throw new InvalidOperationException($"ffmpeg could not write {path}: {error}");
    }

    /// <summary>A sidecar with the user's warp: markers at the tempo from the first downbeat, and a saved default clip.</summary>
    public static AnalysisFile Sidecar(DemoTrack track, DemoStem stem, double gain, long fileSize)
    {
        var secondsPerBeat = 60 / track.Bpm;
        var samples = Waveform(track, stem);
        var tail = 1 / 64.0;
        List<Transient> transients = [];

        for (var step = 0; Lead + step * secondsPerBeat / 4 < track.Seconds - 0.1; step++)
            transients.Add(
                new Transient(
                    (int)((Lead + step * secondsPerBeat / 4) * SampleRate),
                    step % 16 is 0 ? 0.9f
                        : step % 4 is 0 ? 0.6f
                        : 0.3f
                )
            );

        return new AnalysisFile
        {
            Audio = AudioAnalysis.FromSamples(samples, 1, transients, fileSize),
            Warp = new Warp
            {
                Markers =
                [
                    new(Lead, 0),
                    new(Lead + 4 * secondsPerBeat, 4),
                    new(Lead + 4 * secondsPerBeat + tail, 4 + tail * track.Bpm / 60),
                ],
            },
            Clip = new Clip
            {
                LoopStart = 0,
                LoopEnd = Bars(track) * 4,
                OutMarker = Bars(track) * 4,
                LoopOn = false,
                SampleVolume = (float)gain,
            },
            IsDefaultClipSaved = true,
        };
    }

    private static float[] Waveform(DemoTrack track, DemoStem stem)
    {
        var secondsPerBeat = 60 / track.Bpm;
        var samples = new float[track.Seconds * SampleRate];
        var noise = new Random(track.VideoId.GetHashCode(StringComparison.Ordinal) ^ (int)stem);

        for (var i = 0; i < samples.Length; i++)
        {
            var seconds = i / (double)SampleRate - Lead;

            if (seconds < 0)
                continue;

            var beat = (int)(seconds / secondsPerBeat);
            var inBeat = seconds - beat * secondsPerBeat;
            var bar = beat / 4;
            var section = 0.55 + 0.4 * (bar / 8 % 3 is 1 ? 1 : 0.4);

            samples[i] = (float)(
                stem is DemoStem.Instrumental
                    ? Instrumental(inBeat, secondsPerBeat, noise) * section
                    : Vocal(beat, inBeat, seconds, bar) * section
            );
        }

        return samples;
    }

    private static double Instrumental(double inBeat, double secondsPerBeat, Random noise)
    {
        var kick =
            Math.Sin(2 * Math.PI * (48 + 90 * Math.Exp(-inBeat * 30)) * inBeat)
            * Math.Exp(-inBeat * 9);
        var offbeat = Math.Abs(inBeat - secondsPerBeat / 2) < 0.015 ? noise.NextDouble() - 0.5 : 0;

        return Math.Clamp(kick * 0.8 + offbeat * 0.4, -0.95, 0.95);
    }

    private static double Vocal(int beat, double inBeat, double seconds, int bar)
    {
        if (bar % 4 is 3 && beat % 4 >= 2)
            return 0;

        var hz = 220 * Math.Pow(2, (beat * 3 + bar) % 7 / 12.0);
        var envelope = Math.Min(1, inBeat * 40) * Math.Exp(-inBeat * 1.4);

        return Math.Sin(2 * Math.PI * hz * seconds) * envelope * 0.3;
    }
}
