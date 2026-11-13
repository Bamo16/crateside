using Crateside.Core.Ledger;

namespace Crateside.Tests.Ledger;

public sealed class ShareTextTests
{
    private static readonly ShareSubject Subject = new(
        "Artist – Title",
        "abc123DEF45",
        "G♯ minor",
        "HIGH",
        "124.00"
    );

    private static RunEvidence Run() =>
        new()
        {
            Timestamp = DateTimeOffset.UnixEpoch,
            Song = "Artist - Title",
            Reasoning = "The chorus resolves on G# minor.\n\nThe **bass** agrees (r 0.81).",
            TonicCheck = "Not B major: the cadence lands on G#m.",
            Candidates = [new("B major", "The relative; the cadence says no.")],
            Algorithms =
            [
                new()
                {
                    Name = "ffmpeg keydetect",
                    Key = "G# minor",
                    Confidence = 0.8123,
                    Flag = "high",
                    RunnerUpKey = "B major",
                    RunnerUpConfidence = 0.7,
                },
                new() { Name = "madmom", Error = "no output" },
            ],
            Backers = ["madmom"],
            Sources =
            [
                new()
                {
                    Url = "https://www.hooktheory.com/theorytab/view/artist/title",
                    StatesKey = "G# minor",
                    Note = "A transcribed tab.",
                },
            ],
            Chords = ["G#m", "E", "G#m", "E", "G#m", "E"],
            KeyEvents =
            [
                new() { Key = "G# minor", Seconds = 5 },
                new() { Key = "B major", Seconds = 6 },
                new() { Key = "G# minor", Seconds = 30 },
            ],
        };

    [Fact]
    public void The_text_carries_no_table_and_spells_keys_the_way_the_page_does()
    {
        var text = ShareText.Markdown(Subject, Run());

        Assert.DoesNotContain("|", text);
        Assert.Contains("G♯ minor", text);
        Assert.DoesNotContain("G# minor", text);
        Assert.DoesNotContain("The **bass**", text);
    }

    [Fact]
    public void The_text_names_the_song_with_its_video_and_link()
    {
        var text = ShareText.Markdown(Subject, Run());

        Assert.StartsWith("# Artist – Title\n", text);
        Assert.Contains("Video `abc123DEF45` · https://www.youtube.com/watch?v=abc123DEF45", text);
        Assert.Contains("- **Confidence:** HIGH", text);
        Assert.Contains("- **BPM:** 124.00", text);
    }

    [Fact]
    public void Each_detector_reading_shows_its_r_band_and_runner_up_and_a_failure_shows_its_error()
    {
        var text = ShareText.Markdown(Subject, Run());

        Assert.Contains("- **keydetect:** G♯ minor (r 0.812,", text);
        Assert.Contains("runner-up B major 0.700)", text);
        Assert.Contains("- **madmom:** no output", text);
        Assert.Contains("keydetect is backed by madmom only", text);
    }

    [Fact]
    public void The_reasoning_the_tonic_check_and_the_candidates_each_get_a_heading()
    {
        var text = ShareText.Markdown(Subject, Run());

        Assert.Contains(
            "## Why this key\n\nThe chorus resolves on G♯ minor.\n\nThe bass agrees",
            text
        );
        Assert.Contains("## Tonic check\n\nNot B major", text);
        Assert.Contains("## Considered\n\n- **B major:** The relative", text);
    }

    [Fact]
    public void A_source_is_listed_with_its_page_and_note()
    {
        var text = ShareText.Markdown(Subject, Run());

        Assert.Contains("- **www.hooktheory.com**", text);
        Assert.Contains("A transcribed tab.", text);
        Assert.Contains("https://www.hooktheory.com/theorytab/view/artist/title", text);
    }

    [Fact]
    public void The_progression_is_folded_and_the_flicker_is_one_line_not_rows()
    {
        var text = ShareText.Markdown(Subject, Run());

        Assert.Contains("G♯m → E ×3", text);
        Assert.Contains("The running key estimate changed 3 times, 1 of them within 2 s.", text);
    }

    [Fact]
    public void A_run_with_nothing_to_say_still_gives_the_song_and_the_verdict()
    {
        var text = ShareText.Markdown(
            Subject with
            {
                VideoId = null,
                Bpm = null,
            },
            new() { Timestamp = DateTimeOffset.UnixEpoch, Song = "a - b" }
        );

        Assert.DoesNotContain("Video", text);
        Assert.DoesNotContain("BPM", text);
        Assert.Contains("- **Key:** G♯ minor", text);
        Assert.EndsWith("\n", text);
    }

    [Fact]
    public void An_excerpt_cuts_at_a_paragraph_and_ends_with_an_ellipsis()
    {
        var text = $"{new string('a', 50)}\n\n{new string('b', 50)}\n\n{new string('c', 50)}";

        var excerpt = ShareText.Excerpt(text, 120);

        Assert.Equal(3, excerpt.Count);
        Assert.Equal("…", excerpt[^1].ToString());
        Assert.Equal(new string('b', 50), excerpt[1].ToString());
    }

    [Fact]
    public void An_excerpt_within_its_budget_is_the_whole_text()
    {
        Assert.Equal(2, ShareText.Excerpt("one\n\ntwo", 100).Count);
    }

    [Fact]
    public void An_excerpt_keeps_a_first_paragraph_longer_than_the_budget()
    {
        var excerpt = ShareText.Excerpt($"{new string('a', 300)}\n\nnext", 100);

        Assert.Equal(new string('a', 300), excerpt[0].ToString());
        Assert.Equal("…", excerpt[^1].ToString());
    }

    [Fact]
    public void The_saved_file_name_drops_what_a_file_system_refuses() =>
        Assert.Equal(
            "ACDC - Who Made Who - key verdict.png",
            ShareText.FileName("AC/DC - Who: Made? Who")
        );
}
