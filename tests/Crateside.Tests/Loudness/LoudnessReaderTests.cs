using System.Globalization;
using Crateside.Core;
using Crateside.Core.Loudness;
using Crateside.Core.Orchestration.Loop;

namespace Crateside.Tests.Loudness;

public sealed class LoudnessReaderTests : IDisposable
{
    /// <summary>The per-file readings behind the loudness note of 2026-10-19, made with the same filter.</summary>
    private static readonly string MeasuredCsv =
        Environment.SpecialFolder.LocalApplicationData.GetFolderPath(
            "crateside",
            "loudness-2026-10-19.csv"
        );

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    public LoudnessReaderTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void The_integrated_loudness_is_read_from_the_summary() =>
        Assert.Equal(-13.4, LoudnessReader.Parse(FakeEbur128.Summary(-13.4)));

    [Fact]
    public void Silence_reads_as_the_filters_floor() =>
        Assert.Equal(-70, LoudnessReader.Parse(FakeEbur128.Summary(-70)));

    [Fact]
    public void Output_without_a_summary_reads_as_nothing() =>
        Assert.Null(LoudnessReader.Parse("Error opening input file"));

    [Fact]
    public async Task A_reading_is_kept_until_the_file_changes_and_outlives_a_restart()
    {
        var audio = Path.Combine(_root, "Song (Vocal - Full).flac");
        var kept = Path.Combine(_root, "loudness.json");
        await File.WriteAllBytesAsync(audio, [1, 2, 3], TestContext.Current.CancellationToken);
        var ffmpeg = new FakeEbur128(_ => -18.2);
        var reader = new LoudnessReader(ffmpeg, kept);

        await reader.MeasureAsync(audio, TestContext.Current.CancellationToken);
        reader.Save();

        Assert.Equal(-18.2, new LoudnessReader(ffmpeg, kept).Read(audio)?.IntegratedLufs);

        await File.WriteAllBytesAsync(audio, [1, 2, 3, 4], TestContext.Current.CancellationToken);

        Assert.Null(new LoudnessReader(ffmpeg, kept).Read(audio));
        Assert.Equal(1, ffmpeg.Runs);
    }

    [Fact]
    public async Task A_file_ffmpeg_cannot_read_keeps_an_empty_reading()
    {
        var audio = Path.Combine(_root, "Broken (Vocal - Full).flac");
        await File.WriteAllBytesAsync(audio, [1], TestContext.Current.CancellationToken);
        var reader = new LoudnessReader(new FailingFfmpeg());

        await reader.MeasureAsync(audio, TestContext.Current.CancellationToken);

        Assert.Equal(new LoudnessReading(null), reader.Read(audio));
    }

    [Fact]
    public async Task The_real_library_reads_as_the_loudness_note_measured_it()
    {
        if (!File.Exists(MeasuredCsv))
            Assert.Skip($"No measurements at {MeasuredCsv}");

        // Spread across the range, the silent vocal of an instrumental included.
        var measured = Measured()
            .Where(row => File.Exists(row.Path))
            .OrderBy(row => row.Lufs)
            .ToList();

        if (measured.Count < 8)
            Assert.Skip("Too few of the measured files are still here");

        var picked = Enumerable
            .Range(0, 8)
            .Select(i => measured[i * (measured.Count - 1) / 7])
            .ToList();
        var reader = new LoudnessReader(new ProcessRunner());

        foreach (var (path, lufs) in picked)
        {
            if (await reader.MeasureAsync(path, TestContext.Current.CancellationToken) is null)
                Assert.Skip("ffmpeg is not on PATH");

            Assert.Equal(lufs, reader.Read(path)?.IntegratedLufs ?? double.NaN, 0.15);
        }
    }

    private static IEnumerable<(string Path, double Lufs)> Measured() =>
        File.ReadLines(MeasuredCsv)
            .Skip(1)
            .Select(Columns)
            .Where(columns => columns.Count > 2)
            .Select(columns =>
                (columns[1], double.Parse(columns[2], CultureInfo.InvariantCulture))
            );

    /// <summary>A CSV row's fields; a path with a comma in it is quoted.</summary>
    private static List<string> Columns(string line)
    {
        List<string> columns = [];
        var quoted = false;
        var field = new System.Text.StringBuilder();

        foreach (var letter in line)
        {
            if (letter is '"')
                quoted = !quoted;
            else if (letter is ',' && !quoted)
            {
                columns.Add(field.ToString());
                field.Clear();
            }
            else
                field.Append(letter);
        }

        columns.Add(field.ToString());

        return columns;
    }

    private sealed class FailingFfmpeg : IProcessRunner
    {
        public Task<ProcessOutcome> RunAsync(
            ProcessRequest request,
            CancellationToken ct = default
        ) =>
            Task.FromResult(
                new ProcessOutcome
                {
                    ExitCode = 1,
                    StandardOutput = string.Empty,
                    StandardError = "Invalid data found when processing input",
                }
            );
    }
}
