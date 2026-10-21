using Crateside.Core.Orchestration.Loop;

namespace Crateside.Tests.Orchestration.Loop;

public sealed class ProcessRunnerTests
{
    [Fact]
    public async Task A_failing_command_is_returned_with_what_it_said_rather_than_thrown()
    {
        var outcome = OperatingSystem.IsWindows()
            ? await new ProcessRunner().RunAsync(
                new ProcessRequest("cmd", ["/c", "echo out& echo err 1>&2& exit /b 3"]),
                TestContext.Current.CancellationToken
            )
            : await new ProcessRunner().RunAsync(
                new ProcessRequest("sh", ["-c", "echo out; echo err 1>&2; exit 3"]),
                TestContext.Current.CancellationToken
            );

        Assert.Equal(3, outcome.ExitCode);
        Assert.Equal("out", outcome.StandardOutput.Trim());
        Assert.Equal("err", outcome.StandardError.Trim());
    }

    [Fact]
    public async Task Cancelling_stops_the_wait_with_an_operation_cancelled()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            OperatingSystem.IsWindows()
                ? new ProcessRunner().RunAsync(
                    new ProcessRequest("ping", ["-n", "30", "127.0.0.1"]),
                    cancellation.Token
                )
                : new ProcessRunner().RunAsync(
                    new ProcessRequest("sleep", ["30"]),
                    cancellation.Token
                )
        );
    }
}
