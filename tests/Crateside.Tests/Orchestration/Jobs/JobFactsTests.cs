using System.Text.Json;
using Crateside.Core.Orchestration.Jobs;

namespace Crateside.Tests.Orchestration.Jobs;

public sealed class JobFactsTests
{
    private static JudgeFacts Judged(
        string? key = "D major",
        long input = 30_400,
        long cached = 27_000,
        long output = 1_600
    ) => new(key, "high", 3, 5, "model-x", 41.6, input, cached, output);

    [Fact]
    public void A_judge_summary_names_key_confidence_sections_turns_model_time_and_tokens()
    {
        Assert.Equal(
            "D major, HIGH, 3 sections; 5 turns on model-x, 42 s, 30k in (27k cached) · 1.6k out",
            Judged().Text
        );
    }

    [Fact]
    public void A_withheld_key_is_worded_as_withheld()
    {
        Assert.StartsWith("key withheld, HIGH,", Judged(key: null).Text);
    }

    [Fact]
    public void A_summary_with_no_reported_usage_ends_after_the_time()
    {
        Assert.EndsWith("on model-x, 42 s", Judged(input: 0, cached: 0, output: 0).Text);
    }

    [Theory]
    [InlineData(999, "999")]
    [InlineData(1_000, "1.0k")]
    [InlineData(9_949, "9.9k")]
    [InlineData(10_000, "10k")]
    [InlineData(27_500, "28k")]
    public void Token_counts_are_abbreviated_by_size(long tokens, string expected)
    {
        Assert.Equal($", {expected} in (0 cached) · 0 out", JudgeFacts.Tokens(tokens, 0, 0));
    }

    [Fact]
    public void Facts_survive_the_job_log_and_come_back_as_the_same_kind()
    {
        JobFacts[] all = [Judged(), new HandoffFacts(2, 1), new TaggingFacts(4, "A minor")];

        var json = JsonSerializer.Serialize(all);
        var back = JsonSerializer.Deserialize<JobFacts[]>(json)!;

        Assert.Equal(all.Select(facts => facts.Text), back.Select(facts => facts.Text));
        Assert.Equal(
            [typeof(JudgeFacts), typeof(HandoffFacts), typeof(TaggingFacts)],
            back.Select(facts => facts.GetType())
        );
        Assert.Contains("\"kind\":\"handoff\"", json);
    }

    [Theory]
    [InlineData("D major, HIGH, 3 sections, $0.42", "D major, HIGH, 3 sections")]
    [InlineData("D major, 5 turns, $12, 30k in", "D major, 5 turns, 30k in")]
    [InlineData("D major, $ and 5 dollars", "D major, $ and 5 dollars")]
    public void A_dollar_figure_in_an_old_summary_is_dropped_and_nothing_else(
        string stored,
        string expected
    )
    {
        Assert.Equal(expected, LegacyJudgeText.WithoutDollars(stored));
    }
}
