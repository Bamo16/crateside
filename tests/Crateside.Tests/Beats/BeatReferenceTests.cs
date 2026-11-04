using Crateside.Core.Beats;
using Crateside.Core.Orchestration;
using Crateside.Core.Orchestration.Loop;

namespace Crateside.Tests.Beats;

public sealed class BeatReferenceTests : IDisposable
{
    private const string TrackKey = "abcdefghijk";

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    private readonly Dictionary<string, string> _sources = [];
    private readonly string _instrumental;
    private readonly string _source;
    private readonly BeatReader _reader;

    public BeatReferenceTests()
    {
        Directory.CreateDirectory(_root);
        _instrumental = Write("Song (Instrumental - Full).flac");
        _source = Write("source.flac");
        _reader = new BeatReader(new FixedBpm(), sources: new Locator(_sources));
    }

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void The_source_is_the_reference_when_the_upload_has_one()
    {
        _sources[TrackKey] = _source;

        Assert.Equal(_source, _reader.ReferenceFor(TrackKey, _instrumental));
    }

    [Fact]
    public void The_instrumental_is_the_reference_when_there_is_no_source_on_disk()
    {
        Assert.Equal(_instrumental, _reader.ReferenceFor(TrackKey, _instrumental));

        _sources[TrackKey] = Path.Combine(_root, "deleted.flac");

        Assert.Equal(_instrumental, _reader.ReferenceFor(TrackKey, _instrumental));
        Assert.Equal(_instrumental, _reader.ReferenceFor(null, _instrumental));
    }

    [Fact]
    public async Task A_new_reference_is_read_once_and_a_fallback_uses_the_instrumentals_own_reading()
    {
        await _reader.MeasureAsync(_instrumental, TestContext.Current.CancellationToken);
        Assert.Empty(_reader.ToRead([_reader.ReferenceFor(TrackKey, _instrumental)]));

        _sources[TrackKey] = _source;
        var reference = _reader.ReferenceFor(TrackKey, _instrumental);

        Assert.Equal([_source], _reader.ToRead([reference]));
        Assert.Equal(120, _reader.ReadFor(TrackKey, _instrumental)!.Bpm);

        await _reader.MeasureAsync(_source, TestContext.Current.CancellationToken);

        Assert.Empty(_reader.ToRead([reference]));
        Assert.Same(_reader.Read(_source), _reader.ReadFor(TrackKey, _instrumental));

        File.Delete(_source);
        var fallback = _reader.ReferenceFor(TrackKey, _instrumental);

        Assert.Equal(_instrumental, fallback);
        Assert.Same(_reader.Read(_instrumental), _reader.ReadFor(TrackKey, _instrumental));
    }

    [Fact]
    public async Task A_changed_reference_file_is_read_again()
    {
        _sources[TrackKey] = _source;
        await _reader.MeasureAsync(_source, TestContext.Current.CancellationToken);
        Assert.Empty(_reader.ToRead([_source]));

        await File.WriteAllTextAsync(_source, "new audio", TestContext.Current.CancellationToken);

        Assert.Equal([_source], _reader.ToRead([_source]));
    }

    private string Write(string name)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, "audio");

        return path;
    }

    private sealed class Locator(Dictionary<string, string> sources) : ISourceLocator
    {
        public string? PathOf(string trackKey) => sources.GetValueOrDefault(trackKey);
    }

    private sealed class FixedBpm : IProcessRunner
    {
        public Task<ProcessOutcome> RunAsync(
            ProcessRequest request,
            CancellationToken ct = default
        ) =>
            Task.FromResult(
                new ProcessOutcome
                {
                    ExitCode = 0,
                    StandardOutput = "lavfi.beatdetect.bpm=120\nlavfi.beatdetect.confidence=0.9\n",
                    StandardError = string.Empty,
                }
            );
    }
}
