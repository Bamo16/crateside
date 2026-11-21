using Crateside.Components;
using Crateside.Core.SourceOffsets;
using Crateside.Tests.Warps;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Crateside.Tests.Components;

public sealed class SourceOffsetLineTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    private readonly SourceOffsetStore _store = new();

    public SourceOffsetLineTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task An_aligned_source_shows_its_shift_and_a_strip_whose_windows_carry_the_unmatched_spans()
    {
        Save(
            new OffsetReading
            {
                Verdict = OffsetVerdict.Aligned,
                SampleRate = 44_100,
                WindowSeconds = 5,
                Offset = 19_470,
                Windows =
                [
                    new(0, 19_470, 21.5, WindowState.Voting),
                    new(5, 0, 3.2, WindowState.Unmatched),
                ],
                OnlyInSource = [new(5, 10)],
                OnlyInMix = [new(6, 9)],
            }
        );

        var html = await RenderAsync();

        Assert.Contains("Aligned to the mix: &#x2B;0:00:441", html);
        Assert.Matches(
            "class=\"cell unmatched\"[^>]*title=\"[^\"]*only in this file: 5-10 s",
            html
        );
        Assert.Matches("class=\"cell unmatched\"[^>]*title=\"[^\"]*only in the mix: 6-9 s", html);
        Assert.DoesNotContain("<p class=\"spans\"", html);
        Assert.DoesNotContain("<table", html);
        Assert.Matches(
            "class=\"cell voting\"[^>]*title=\"0 s [^\"]*offset 19470 samples[^\"]*PSR 21.5",
            html
        );
        Assert.Matches(
            "class=\"cell unmatched\"[^>]*title=\"5 s [^\"]*offset 0 samples[^\"]*PSR 3.2",
            html
        );
        Assert.Contains("matches elsewhere in the mix", html);
    }

    [Theory]
    [InlineData(OffsetVerdict.Edit, "Cut differently from the mix")]
    [InlineData(OffsetVerdict.Drift, "Runs at another speed than the mix")]
    [InlineData(OffsetVerdict.NotTheSameRecording, "Does not line up with the mix")]
    public async Task A_refused_source_shows_why_and_that_it_is_not_written(
        OffsetVerdict verdict,
        string line
    )
    {
        Save(
            new OffsetReading
            {
                Verdict = verdict,
                SampleRate = 44_100,
                WindowSeconds = 5,
                DriftPpm = verdict is OffsetVerdict.Drift ? 20 : null,
                Runs = verdict is OffsetVerdict.Edit ? [new(0, 25, 0), new(30, 60, 66_150)] : [],
            }
        );

        var html = await RenderAsync();

        Assert.Contains(line, html);
        Assert.Contains("not written", html);
        Assert.Contains("refused", html);
    }

    [Fact]
    public async Task A_source_not_measured_yet_says_it_waits()
    {
        Assert.Contains("Waits to be measured against the mix.", await RenderAsync());
    }

    [Fact]
    public async Task A_source_whose_file_changed_since_it_was_measured_says_it_is_measured_again()
    {
        var reading = new OffsetReading
        {
            Verdict = OffsetVerdict.Aligned,
            SampleRate = 44_100,
            WindowSeconds = 5,
            Offset = 0,
        };

        var source = Save(reading);
        File.AppendAllText(source, "more");

        Assert.Contains("is measured again shortly", await RenderAsync());
    }

    private string Save(OffsetReading reading)
    {
        var (mix, source) = (Path.Combine(_root, "mix.flac"), Path.Combine(_root, "source.flac"));

        File.WriteAllText(mix, "audio");
        File.WriteAllText(source, "audio");
        _store.Save(
            "added",
            new SourceOffsetRecord(
                "main",
                FileStamp.Of(mix)!,
                FileStamp.Of(source)!,
                reading,
                DateTimeOffset.UnixEpoch
            )
        );

        return source;
    }

    [Fact]
    public async Task A_source_moved_since_it_was_measured_keeps_its_line()
    {
        var source = Save(
            new OffsetReading
            {
                Verdict = OffsetVerdict.Aligned,
                SampleRate = 44_100,
                WindowSeconds = 5,
                Offset = 19_470,
            }
        );
        var moved = Path.Combine(_root, "library", "source.flac");
        Directory.CreateDirectory(Path.GetDirectoryName(moved)!);
        File.Move(source, moved);

        var html = await RenderAsync(moved);

        Assert.Contains("Aligned to the mix", html);
        Assert.DoesNotContain("measured again", html);
    }

    private async Task<string> RenderAsync(string? path = null)
    {
        await using var services = new ServiceCollection()
            .AddSingleton(_store)
            .AddSingleton(new SourceOffsetPass(SteadyHits.Decoder, _store, TimeProvider.System))
            .BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);

        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<SourceOffsetLine>(
                ParameterView.FromDictionary(
                    new Dictionary<string, object?>
                    {
                        ["SourceKey"] = "added",
                        ["Path"] = path ?? Path.Combine(_root, "source.flac"),
                    }
                )
            );

            return output.ToHtmlString();
        });
    }
}
