using Crateside.Core.Orchestration.Jobs;
using Microsoft.Extensions.Time.Testing;

namespace Crateside.Tests.Orchestration.Jobs;

public sealed class StageNotesTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void Notes_come_back_oldest_first_for_the_uploads_asked_about_only()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero));
        var notes = new StageNotes(Path.Combine(_root, "stage-notes.jsonl"), time);

        notes.Record("a", "Warp", "starter warp at 190.00 BPM, marker at 0.75 s");
        time.Advance(TimeSpan.FromMinutes(1));
        notes.Record("b", "Loudness", "clip gain −3.2 dB to −14 LUFS");
        time.Advance(TimeSpan.FromMinutes(1));
        notes.Record("a", "Loudness", "clip gain −3.2 dB to −14 LUFS");

        Assert.Equal(
            ["Warp", "Loudness"],
            notes.For(new HashSet<string> { "a" }).Select(note => note.Stage)
        );
        Assert.Empty(notes.For(new HashSet<string> { "c" }));
    }

    [Fact]
    public void A_missing_file_holds_no_notes()
    {
        Assert.Empty(new StageNotes(Path.Combine(_root, "none.jsonl")).All());
    }
}
