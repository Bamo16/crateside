using Crateside.Core.Orchestration.Loop;
using Crateside.Core.Orchestration.Verdict;
using Microsoft.Extensions.Options;

namespace Crateside.Tests.Orchestration.Verdict;

public sealed class JudgeAvailabilityTests
{
    private static JudgeAvailability Availability(bool ownLogin = true) =>
        new(
            Options.Create(
                new JudgeOptions
                {
                    Environment = ownLogin
                        ? []
                        : new() { ["ANTHROPIC_BASE_URL"] = "https://example.test" },
                }
            )
        );

    private static ProcessOutcome Outcome(
        string output = "",
        string error = "",
        int exitCode = 1
    ) =>
        new()
        {
            ExitCode = exitCode,
            StandardOutput = output,
            StandardError = error,
        };

    [Fact]
    public void A_spent_allowance_on_claude_codes_own_login_pauses_judging()
    {
        var result = Availability().Unavailable(Outcome(error: "You've hit your limit"));

        Assert.True(result is { IsAgentUnavailable: true, IsRefused: false });
    }

    [Fact]
    public void A_rate_limit_on_another_endpoint_holds_judging_for_the_wait_it_stated()
    {
        var result = Availability(ownLogin: false)
            .Unavailable(
                Outcome(output: """{"api_error_status": 429}""", error: "pause for 90 seconds")
            );

        Assert.True(
            result is { IsRefused: true, RetryAfter: { } wait } && wait == TimeSpan.FromSeconds(90)
        );
    }

    [Fact]
    public void A_stated_wait_is_capped_at_an_hour()
    {
        var result = Availability(ownLogin: false)
            .Unavailable(
                Outcome(output: """{"api_error_status": 429}""", error: "pause for 99999 seconds")
            );

        Assert.Equal(TimeSpan.FromHours(1), result?.RetryAfter);
    }

    [Fact]
    public void A_request_the_endpoint_could_not_take_is_about_the_track()
    {
        var output =
            """{"type":"result","is_error":true,"api_error_status":422,"result":"API Error: 422 bad"}""";

        Assert.Null(Availability().Unavailable(Outcome(output)));
    }

    [Fact]
    public void A_final_reply_of_done_is_no_api_error()
    {
        var output = """{"type":"result","subtype":"success","is_error":false,"result":"done"}""";

        Assert.Null(JudgeAvailability.ApiError(Outcome(output, exitCode: 0)));
        Assert.Null(Availability().Unavailable(Outcome(output, exitCode: 0)));
    }
}
