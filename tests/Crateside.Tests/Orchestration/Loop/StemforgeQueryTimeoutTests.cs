using Crateside.Core.Naming;
using Crateside.Core.Orchestration.Download;
using Crateside.Core.Orchestration.Edits;
using Crateside.Core.Orchestration.Loop;
using Crateside.Core.Orchestration.Separation;
using Microsoft.Extensions.Logging;

namespace Crateside.Tests.Orchestration.Loop;

public sealed class StemforgeQueryTimeoutTests
{
    private const string Url = "https://www.youtube.com/watch?v=abcdefghijk";

    [Fact]
    public async Task The_preset_query_is_asked_with_the_query_timeout_and_a_hang_reads_as_no_presets()
    {
        var runner = new HungRunner();

        var catalog = await new PresetCatalogReader(runner).ReadAsync(
            TestContext.Current.CancellationToken
        );

        Assert.True(catalog.IsEmpty);
        Assert.Equal(ProcessRequest.QueryTimeout, runner.Request?.Timeout);
        Assert.Equal(TimeSpan.FromSeconds(60), runner.Request?.Timeout);
    }

    [Fact]
    public async Task A_hung_list_formats_query_leaves_the_upload_unreadable_and_the_name_unresolved()
    {
        var probeRunner = new HungRunner();
        var nameRunner = new HungRunner();

        var probed = await new SourceProbe(probeRunner).ReadAsync(
            Url,
            TestContext.Current.CancellationToken
        );
        var named = await new NameResolver(nameRunner).ResolveAsync(
            Url,
            TestContext.Current.CancellationToken
        );

        Assert.NotNull(probed.Problem);
        Assert.Null(named);
        Assert.Equal(ProcessRequest.QueryTimeout, probeRunner.Request?.Timeout);
        Assert.Equal(ProcessRequest.QueryTimeout, nameRunner.Request?.Timeout);
    }

    [Fact]
    public async Task A_hung_tools_query_leaves_the_playlist_unreadable()
    {
        var runner = new HungRunner();

        var listing = await new PlaylistExpander(runner).ListAsync(
            "https://www.youtube.com/playlist?list=PLabcdefghij",
            TestContext.Current.CancellationToken
        );

        Assert.IsType<PlaylistListing.Unreadable>(listing);
        Assert.Equal(["tools", "--json"], runner.Request?.Arguments);
        Assert.Equal(ProcessRequest.QueryTimeout, runner.Request?.Timeout);
    }

    [Fact]
    public async Task A_command_that_outlives_its_timeout_is_killed_and_logged_with_its_command_line()
    {
        var log = new LineLogger();
        var request = OperatingSystem.IsWindows()
            ? new ProcessRequest("ping", ["-n", "30", "127.0.0.1"])
            : new ProcessRequest("sleep", ["30"]);

        var outcome = await new ProcessRunner(log).RunAsync(
            request with
            {
                Timeout = TimeSpan.FromMilliseconds(300),
            },
            TestContext.Current.CancellationToken
        );

        Assert.True(outcome.TimedOut);
        Assert.False(outcome.Succeeded);
        Assert.Contains(log.Lines, line => line.Contains(request.CommandLine));
        Assert.Equal(LogLevel.Warning, log.Level);
    }

    [Fact]
    public async Task A_command_that_finishes_inside_its_timeout_is_not_marked_timed_out()
    {
        var request = OperatingSystem.IsWindows()
            ? new ProcessRequest("cmd", ["/c", "echo done"])
            : new ProcessRequest("sh", ["-c", "echo done"]);

        var outcome = await new ProcessRunner().RunAsync(
            request with
            {
                Timeout = TimeSpan.FromSeconds(30),
            },
            TestContext.Current.CancellationToken
        );

        Assert.False(outcome.TimedOut);
        Assert.Equal("done", outcome.StandardOutput.Trim());
    }

    private sealed class HungRunner : IProcessRunner
    {
        public ProcessRequest? Request { get; private set; }

        public Task<ProcessOutcome> RunAsync(ProcessRequest request, CancellationToken ct = default)
        {
            Request = request;

            return Task.FromResult(
                new ProcessOutcome
                {
                    ExitCode = -1,
                    StandardOutput = string.Empty,
                    StandardError = "did not finish in 60 s",
                    TimedOut = true,
                }
            );
        }
    }

    private sealed class LineLogger : ILogger<ProcessRunner>
    {
        public List<string> Lines { get; } = [];

        public LogLevel Level { get; private set; }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            Level = logLevel;
            Lines.Add(formatter(state, exception));
        }
    }
}
