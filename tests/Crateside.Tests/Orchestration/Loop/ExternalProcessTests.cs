using Crateside.Core.Orchestration.Loop;

namespace Crateside.Tests.Orchestration.Loop;

public sealed class ExternalProcessTests
{
    [Fact]
    public async Task A_failing_command_is_returned_with_what_it_said_rather_than_thrown()
    {
        var outcome = OperatingSystem.IsWindows()
            ? await ExternalProcess.RunAsync(
                "cmd",
                ["/c", "echo out& echo err 1>&2& exit /b 3"],
                ct: TestContext.Current.CancellationToken
            )
            : await ExternalProcess.RunAsync(
                "sh",
                ["-c", "echo out; echo err 1>&2; exit 3"],
                ct: TestContext.Current.CancellationToken
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
                ? ExternalProcess.RunAsync(
                    "ping",
                    ["-n", "30", "127.0.0.1"],
                    ct: cancellation.Token
                )
                : ExternalProcess.RunAsync("sleep", ["30"], ct: cancellation.Token)
        );
    }
}
