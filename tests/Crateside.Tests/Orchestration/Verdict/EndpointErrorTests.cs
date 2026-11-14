using Crateside.Core.Orchestration.Verdict;

namespace Crateside.Tests.Orchestration.Verdict;

public sealed class EndpointErrorTests
{
    [Fact]
    public void The_result_is_read_from_the_sessions_json_with_its_escapes_undone()
    {
        const string stdout = """{"type":"result","is_error":true,"result":"say \"hi\" now"}""";

        Assert.Equal("say \"hi\" now", EndpointError.ResultOf(stdout));
    }

    [Fact]
    public void A_session_with_no_result_has_no_words()
    {
        Assert.Null(EndpointError.ResultOf("not json"));
        Assert.Null(EndpointError.WordsOf(null));
        Assert.Null(EndpointError.WordsOf("Failed to authenticate. API Error: 401"));
    }

    [Theory]
    [InlineData("Failed to authenticate. API Error: 401 Invalid API key.", "Invalid API key.")]
    [InlineData("API Error: 401 DeepSeek session expired.", "DeepSeek session expired.")]
    [InlineData("DeepSeek session expired.", "DeepSeek session expired.")]
    [InlineData(
        """API Error: 401 {"type":"error","error":{"message":"Log in again."}}""",
        "Log in again."
    )]
    public void The_endpoints_words_are_what_is_left_of_claude_codes_lead_in(
        string result,
        string words
    ) => Assert.Equal(words, EndpointError.WordsOf(result));
}
