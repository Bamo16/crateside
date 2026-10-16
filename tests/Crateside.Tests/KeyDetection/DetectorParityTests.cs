using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Crateside.Core;
using Crateside.Core.KeyDetection;
using Crateside.Core.Orchestration.Loop;

namespace Crateside.Tests.KeyDetection;

/// <summary>
/// Each port against its Python original's key on the bake-off's tracks (#113). The audio is not in
/// the repo: point <c>CRATESIDE_PARITY_AUDIO</c> at a folder of <c>&lt;id&gt;.flac</c>.
/// </summary>
public sealed partial class DetectorParityTests(ITestOutputHelper output)
{
    private const string AudioVariable = "CRATESIDE_PARITY_AUDIO";

    [Fact]
    public async Task Both_ports_name_the_key_their_Python_originals_named()
    {
        var folder = Environment.GetEnvironmentVariable(AudioVariable);
        Assert.SkipWhen(folder is not { Length: > 0 }, $"{AudioVariable} is not set");

        var tracks = BakeOff()
            .Select(row => (row, Audio: AudioFor(folder!, row)))
            .Where(track => track.Audio is not null)
            .ToList();
        Assert.SkipWhen(tracks.Count is 0, $"no bake-off audio in {folder}");

        var decoder = new PcmDecoder(new ProcessRunner(), "ffmpeg");
        using var madmom = new MadmomKeyCnn();
        List<string> differences = [];

        foreach (var (row, audio) in tracks)
        {
            var decoded = Assert.IsType<DecodeOutcome.Decoded>(
                await decoder.DecodeAsync(
                    audio!,
                    window: null,
                    TestContext.Current.CancellationToken
                )
            );

            var clock = Stopwatch.StartNew();
            var cnn = Assert.IsType<KeyReading.Read>(madmom.Read(decoded.Samples));
            var cnnTime = clock.Elapsed;
            clock.Restart();
            var edma = Assert.IsType<KeyReading.Read>(EdmaKeyEstimator.Read(decoded.Samples));
            var edmaTime = clock.Elapsed;

            output.WriteLine(
                $"{row.Id}\tmadmom {cnn.Key.LedgerName} {cnn.Confidence:F5} ({cnnTime.TotalSeconds:F2} s)"
                    + $"\tedma {edma.Key.LedgerName} {edma.Confidence:F5} ({edmaTime.TotalSeconds:F2} s)"
            );

            if (cnn.Key.LedgerName != row.Madmom)
                differences.Add($"{row.Id}: madmom {cnn.Key.LedgerName}, Python {row.Madmom}");
            if (edma.Key.LedgerName != row.Edma)
                differences.Add($"{row.Id}: edma {edma.Key.LedgerName}, Python {row.Edma}");
        }

        Assert.Empty(differences);
    }

    /// <summary>A corpus track is named by its id; a ledger song, whose id is its title, by its video id.</summary>
    private static string? AudioFor(string folder, BakeOffRow row) =>
        new[] { row.Id, YouTubeUrl.TryGetVideoId(row.SourceUrl, out var videoId) ? videoId : null }
            .OfType<string>()
            .Select(name => Path.Combine(folder, $"{name}.flac"))
            .FirstOrDefault(File.Exists);

    private static IEnumerable<BakeOffRow> BakeOff()
    {
        var lines = File.ReadAllLines(
            Path.Combine(RepoRoot(), "docs", "notes", "2026-10-15-the-detector-bake-off.csv")
        );
        var header = Fields(lines[0]);
        var (id, url, madmom, edma) = (
            header.IndexOf("id"),
            header.IndexOf("source_url"),
            header.IndexOf("madmom"),
            header.IndexOf("ess_edma")
        );

        return lines[1..]
            .Select(Fields)
            .Select(fields => new BakeOffRow(
                fields[id],
                fields[url],
                fields[madmom],
                fields[edma]
            ));
    }

    private static List<string> Fields(string line) =>
        [
            .. CsvField
                .Matches(line)
                .Select(match =>
                    match.Groups["Quoted"] is { Success: true, Value: var quoted }
                        ? quoted
                        : match.Groups["Plain"].Value
                ),
        ];

    private sealed record BakeOffRow(string Id, string SourceUrl, string Madmom, string Edma);

    private static string RepoRoot([CallerFilePath] string here = "")
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(here)!);

        while (!File.Exists(Path.Combine(directory.FullName, "Crateside.slnx")))
            directory = directory.Parent!;

        return directory.FullName;
    }

    [GeneratedRegex(@"(?<=^|,)(""(?<Quoted>[^""]*)""|(?<Plain>[^,]*))")]
    private static partial Regex CsvField { get; }
}
