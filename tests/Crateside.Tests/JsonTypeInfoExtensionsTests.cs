using Crateside.Core;
using Crateside.Core.Orchestration.Loop;

namespace Crateside.Tests;

public sealed class JsonTypeInfoExtensionsTests : IDisposable
{
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(),
        $"crateside-json-{Guid.NewGuid():N}"
    );

    private string PathOf(string name) => Path.Combine(_folder, name);

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public void A_missing_file_reads_as_null() =>
        Assert.Null(
            PauseJsonContext.Default.PausedStages.ReadFileOrDefault(PathOf("missing.json"))
        );

    [Theory]
    [InlineData("{ not json")]
    [InlineData("null")]
    public void A_malformed_file_or_a_null_literal_reads_as_null(string content)
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(PathOf("bad.json"), content);

        Assert.Null(PauseJsonContext.Default.PausedStages.ReadFileOrDefault(PathOf("bad.json")));
    }

    [Fact]
    public void A_write_round_trips_and_leaves_no_temporary_file()
    {
        var path = PathOf("nested/paused.json");
        var stages = new PausedStages { Judging = true, Backfill = true };

        PauseJsonContext.Default.PausedStages.WriteFile(path, stages);

        Assert.Equal(stages, PauseJsonContext.Default.PausedStages.ReadFileOrDefault(path));
        Assert.Equal(
            ["paused.json"],
            Directory.GetFiles(Path.GetDirectoryName(path)!).Select(Path.GetFileName)
        );
    }

    [Fact]
    public void A_write_replaces_what_was_there()
    {
        var path = PathOf("paused.json");

        PauseJsonContext.Default.PausedStages.WriteFile(
            path,
            new PausedStages { Separation = true }
        );
        PauseJsonContext.Default.PausedStages.WriteFile(path, new PausedStages { Judging = true });

        Assert.Equal(
            new PausedStages { Judging = true },
            PauseJsonContext.Default.PausedStages.ReadFileOrDefault(path)
        );
    }
}
