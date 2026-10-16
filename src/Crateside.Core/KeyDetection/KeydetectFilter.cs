using System.ComponentModel;
using System.Globalization;
using System.Text.RegularExpressions;
using Crateside.Core.Beats;
using Crateside.Core.Ledger;
using Crateside.Core.Orchestration.Loop;
using Crateside.Core.Pairing;

namespace Crateside.Core.KeyDetection;

/// <summary>FiLL's <c>keydetect</c> filter in the NoMercy ffmpeg build: a key, its correlation, and the chords under it.</summary>
public sealed partial class KeydetectFilter(IProcessRunner runner, string ffmpeg)
{
    public const string Name = "ffmpeg_keydetect";

    private (string Version, string? Sha256)? _build;

    /// <summary>ffmpeg's version line and the executable's hash: <c>r</c>'s trust bands differ between builds.</summary>
    public async Task<(string? Version, string? Sha256)> IdentifyAsync(
        CancellationToken ct = default
    )
    {
        if (_build is { } known)
            return known;

        ProcessOutcome outcome;
        try
        {
            outcome = await runner.RunAsync(
                new ProcessRequest(ffmpeg, ["-hide_banner", "-version"]),
                ct
            );
        }
        catch (Win32Exception)
        {
            return (null, null);
        }

        if (
            outcome is not { Succeeded: true, StandardOutput: var output }
            || output.Split('\n')[0].Trim() is not { Length: > 0 } version
        )
            return (null, null);

        var sha256 = FfmpegBuild.Located(ffmpeg) is { Exists: true } executable
            ? await FfmpegBuild.HashAsync(executable, ct)
            : null;
        _build = (version, sha256);

        return (version, sha256);
    }

    public async Task<KeydetectReading> ReadAsync(
        string audioPath,
        RunWindow? window,
        CancellationToken ct = default
    )
    {
        ProcessOutcome outcome;

        try
        {
            outcome = await runner.RunAsync(
                new ProcessRequest(
                    ffmpeg,
                    [
                        "-nostdin",
                        "-hide_banner",
                        "-nostats",
                        .. PcmDecoder.WindowArguments(window),
                        "-i",
                        audioPath,
                        "-vn",
                        "-af",
                        "keydetect",
                        "-f",
                        "null",
                        "-",
                    ]
                ),
                ct
            );
        }
        catch (Win32Exception ex)
        {
            return new KeydetectReading.Failed($"ffmpeg could not be started: {ex.Message}");
        }

        return outcome.Succeeded
            ? Parse(outcome.StandardError)
            : new KeydetectReading.Failed(
                ExternalProcess.Describe(outcome.StandardError, outcome.StandardOutput)
                    ?? $"ffmpeg exited with {outcome.ExitCode}"
            );
    }

    /// <summary>Reads the summary the filter prints at the end of the stream, and the key changes before it.</summary>
    internal static KeydetectReading Parse(string stderr)
    {
        if (
            FinalKey.Matches(stderr) is not [.., var keyLine]
            || TryParseToken(keyLine.Groups["Key"].Value) is not { } key
        )
            return new KeydetectReading.Failed(
                "no lavfi.keydetect.key in ffmpeg's output: silent input, or a build without the filter"
            );

        var r = Confidence.Matches(stderr) is [.., var confidenceLine]
            ? double.Parse(confidenceLine.Groups["R"].Value, CultureInfo.InvariantCulture)
            : 0;

        IReadOnlyList<string> chords = FinalChords.Matches(stderr) is [.., var chordLine]
            ? Collapsed(chordLine.Groups["Chords"].Value.Split('-'))
            : [];

        return new KeydetectReading.Read(
            key,
            r,
            chords,
            [
                .. KeyEvent
                    .Matches(stderr)
                    .Select(match => new KeydetectEvent(
                        double.Parse(match.Groups["Seconds"].Value, CultureInfo.InvariantCulture),
                        match.Groups["Key"].Value,
                        double.Parse(match.Groups["R"].Value, CultureInfo.InvariantCulture)
                    )),
            ]
        );
    }

    /// <summary>The filter's spelling: <c>F#m</c> is F♯ minor, <c>F#</c> F♯ major.</summary>
    internal static MusicKey? TryParseToken(string token) =>
        token switch
        {
            [.. var tonic, 'm'] => MusicKey.FromString($"{tonic} minor"),
            { Length: > 0 } => MusicKey.FromString($"{token} major"),
            _ => null,
        };

    private static List<string> Collapsed(IEnumerable<string> chords) =>
        chords.Aggregate(
            new List<string>(),
            (kept, chord) =>
            {
                if (kept is not [.., var last] || last != chord)
                    kept.Add(chord);

                return kept;
            }
        );

    [GeneratedRegex(@"lavfi\.keydetect\.key=(?<Key>[A-G][#b]?m?)\s*$", RegexOptions.Multiline)]
    private static partial Regex FinalKey { get; }

    [GeneratedRegex(@"lavfi\.keydetect\.key_confidence=(?<R>[0-9.+-]+)")]
    private static partial Regex Confidence { get; }

    [GeneratedRegex(@"lavfi\.keydetect\.chords=(?<Chords>[A-Gb#mN-]+)")]
    private static partial Regex FinalChords { get; }

    [GeneratedRegex(@"t=(?<Seconds>[0-9.]+) key=(?<Key>[A-G][#b]?m?) \(r=(?<R>-?[0-9.]+)\)")]
    private static partial Regex KeyEvent { get; }
}

/// <summary>What <c>keydetect</c> made of the audio, or why it made nothing.</summary>
public abstract record KeydetectReading
{
    private KeydetectReading() { }

    /// <param name="R">The correlation of the key's profile with the audio's chroma.</param>
    /// <param name="Chords">The committed progression, repeats collapsed, in the filter's spelling.</param>
    /// <param name="KeyEvents">Every change of the running estimate as the stream played.</param>
    public sealed record Read(
        MusicKey Key,
        double R,
        IReadOnlyList<string> Chords,
        IReadOnlyList<KeydetectEvent> KeyEvents
    ) : KeydetectReading;

    public sealed record Failed(string Error) : KeydetectReading;
}

/// <summary>A change of <c>keydetect</c>'s running estimate, in the filter's spelling.</summary>
public sealed record KeydetectEvent(double Seconds, string Key, double R);
