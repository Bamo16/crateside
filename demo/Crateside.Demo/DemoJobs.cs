using System.Globalization;
using Crateside.Core.Orchestration.Jobs;
using Humanizer;

namespace Crateside.Demo;

internal sealed record DemoNote(string Pass, string Summary);

internal sealed record DemoAction(UserActionKind Kind, string? Detail);

/// <summary>One line the app would have written as it worked: a job update, a stage note or something the user did.</summary>
internal sealed record DemoStep(
    string VideoId,
    DateTimeOffset At,
    JobStage Stage = default,
    JobStatus Status = default,
    string? Error = null,
    DateTimeOffset? HeldUntil = null,
    string? Summary = null,
    DemoNote? Note = null,
    DemoAction? Action = null
);

/// <summary>When each stage of each made-up track ran, so a track at a given stage has exactly the history that leads to it.</summary>
internal static class DemoJobs
{
    /// <summary>Tracks finished per ISO week, oldest first, ending in the current week: a ramp, a peak, a dip and a rebound.</summary>
    private static readonly int[] WeeklyFinished = [4, 9, 17, 28, 41, 63, 74, 52, 38, 57, 26];

    /// <summary>
    /// Finished tracks from earlier weeks, with synthetic IDs the library knows nothing of, so only
    /// the Dashboard's history reads them. The demo tracks already finished count towards their weeks.
    /// </summary>
    public static IEnumerable<DemoStep> History(IReadOnlyList<DemoStep> demo, DateTimeOffset now)
    {
        var thisWeek = MondayOf(now);
        var already = demo.Where(step =>
                step is { Stage: JobStage.Tagging, Status: JobStatus.Succeeded }
            )
            .Select(step => MondayOf(step.At))
            .CountBy(monday => monday)
            .ToDictionary();
        var number = 0;

        for (var i = 0; i < WeeklyFinished.Length; i++)
        {
            var monday = thisWeek.AddDays(7 * (i - (WeeklyFinished.Length - 1)));
            var start = new DateTimeOffset(monday, TimeZoneInfo.Local.GetUtcOffset(monday));
            var end =
                i == WeeklyFinished.Length - 1 ? now - TimeSpan.FromHours(2) : start.AddDays(7);
            var span = end - start;
            var count = Math.Max(0, WeeklyFinished[i] - already.GetValueOrDefault(monday));

            for (var j = 0; j < count; j++)
            {
                number++;

                // Evenly spread, nudged by a fixed figure so no two weeks line up.
                var jitter = number * 7919 % 100 / 100.0;
                var tagged = start + span * ((j + jitter) / count);

                foreach (var step in Finished($"DemoHistory{number:D3}", tagged, number))
                    yield return step;
            }
        }
    }

    private static DateTime MondayOf(DateTimeOffset at)
    {
        var local = at.ToLocalTime().DateTime;

        return ISOWeek.ToDateTime(
            ISOWeek.GetYear(local),
            ISOWeek.GetWeekOfYear(local),
            DayOfWeek.Monday
        );
    }

    private static IEnumerable<DemoStep> Finished(string id, DateTimeOffset tagged, int number)
    {
        var at = tagged - TimeSpan.FromMinutes(40);

        DemoStep Step(JobStage stage, JobStatus status, TimeSpan after, string? error = null)
        {
            at += after;

            return new DemoStep(id, at, stage, status, Error: error);
        }

        yield return Step(JobStage.Download, JobStatus.Running, TimeSpan.Zero);
        yield return Step(JobStage.Download, JobStatus.Succeeded, TimeSpan.FromSeconds(24));
        yield return Step(JobStage.Evidence, JobStatus.Running, TimeSpan.FromSeconds(1));
        yield return Step(JobStage.Evidence, JobStatus.Succeeded, TimeSpan.FromSeconds(7));
        yield return Step(JobStage.Verdict, JobStatus.Running, TimeSpan.FromMinutes(20));

        // A canceled judging is put back in line, and a failed separation is run again.
        if (number % 40 is 7)
        {
            yield return Step(
                JobStage.Verdict,
                JobStatus.Canceled,
                TimeSpan.FromMinutes(1),
                "put back in line by hand"
            );
            yield return Step(JobStage.Verdict, JobStatus.Running, TimeSpan.FromMinutes(2));
        }

        yield return Step(JobStage.Verdict, JobStatus.Succeeded, TimeSpan.FromMinutes(2));
        yield return Step(JobStage.Separation, JobStatus.Running, TimeSpan.FromSeconds(1));

        if (number % 25 is 3)
        {
            yield return Step(
                JobStage.Separation,
                JobStatus.Failed,
                TimeSpan.FromMinutes(2),
                "the separator ran out of memory"
            );
            yield return Step(JobStage.Separation, JobStatus.Running, TimeSpan.FromMinutes(1));
        }

        yield return Step(JobStage.Separation, JobStatus.Succeeded, TimeSpan.FromMinutes(6));
        yield return Step(JobStage.Handoff, JobStatus.Running, TimeSpan.FromSeconds(1));
        yield return Step(JobStage.Handoff, JobStatus.Succeeded, TimeSpan.FromSeconds(56));
        yield return Step(JobStage.Tagging, JobStatus.Running, TimeSpan.FromSeconds(1));

        yield return new DemoStep(id, tagged, JobStage.Tagging, JobStatus.Succeeded);
    }

    public static IEnumerable<DemoStep> Timeline(
        IReadOnlyList<DemoTrack> tracks,
        DateTimeOffset now
    ) => tracks.SelectMany(track => Of(track, now - TimeSpan.FromHours(track.HoursAgo), now));

    private static IEnumerable<DemoStep> Of(
        DemoTrack track,
        DateTimeOffset start,
        DateTimeOffset now
    )
    {
        if (track.Stage is DemoStage.Waiting)
            yield break;

        var id = track.VideoId;
        var at = start;

        DemoStep Step(JobStage stage, JobStatus status, TimeSpan after, string? summary = null)
        {
            at += after;

            return new DemoStep(id, at, stage, status, Summary: summary);
        }

        yield return Step(JobStage.Download, JobStatus.Running, TimeSpan.FromMinutes(1));
        yield return Step(
            JobStage.Download,
            JobStatus.Succeeded,
            TimeSpan.FromSeconds(24),
            "downloaded m4a 256 kbps (format 141)"
        );
        yield return Step(JobStage.Evidence, JobStatus.Running, TimeSpan.FromSeconds(1));
        yield return Step(
            JobStage.Evidence,
            JobStatus.Succeeded,
            TimeSpan.FromSeconds(7),
            "keydetect, madmom and Essentia read the whole track"
        );

        if (track.Stage is DemoStage.Judging)
        {
            yield return new DemoStep(
                id,
                now - TimeSpan.FromMinutes(3),
                JobStage.Verdict,
                JobStatus.Running
            );

            yield break;
        }

        if (track.Stage is DemoStage.Held)
        {
            yield return new DemoStep(
                id,
                now - TimeSpan.FromMinutes(6),
                JobStage.Verdict,
                JobStatus.Held,
                Error: "the judge's endpoint refused the session; judging holds and tries again",
                HeldUntil: now + TimeSpan.FromMinutes(24)
            );

            yield break;
        }

        yield return Step(JobStage.Verdict, JobStatus.Running, TimeSpan.FromMinutes(25));
        yield return Step(
            JobStage.Verdict,
            JobStatus.Succeeded,
            TimeSpan.FromMinutes(2),
            $"{track.TaggedKey ?? "no key"} · {track.Confidence.ToString().ToUpperInvariant()} · {"section".ToQuantity(Math.Max(1, track.Verdict!.Sections.Count))} · 36.8k tokens"
        );

        if (track.Stage is DemoStage.JudgedInLine)
            yield break;

        if (track.Stage is DemoStage.Separating)
        {
            yield return new DemoStep(
                id,
                now - TimeSpan.FromMinutes(3),
                JobStage.Separation,
                JobStatus.Running
            );

            yield break;
        }

        yield return Step(JobStage.Separation, JobStatus.Running, TimeSpan.FromSeconds(1));
        yield return Step(
            JobStage.Separation,
            JobStatus.Succeeded,
            TimeSpan.FromMinutes(6),
            "wrote Vocal - Full and Instrumental - Full"
        );

        if (track.Stage is DemoStage.Parked)
            yield break;

        yield return Step(JobStage.Handoff, JobStatus.Running, TimeSpan.FromSeconds(1));
        yield return Step(
            JobStage.Handoff,
            JobStatus.Succeeded,
            TimeSpan.FromSeconds(56),
            "staged 2 stems"
        );
        yield return Step(JobStage.Tagging, JobStatus.Running, TimeSpan.FromSeconds(1));
        yield return Step(
            JobStage.Tagging,
            JobStatus.Succeeded,
            TimeSpan.FromSeconds(1),
            $"tagged 2 stems {track.TaggedKey}"
        );

        if (track.Stage is DemoStage.ReadyToWarp)
            yield break;

        if (track.Stage is DemoStage.Library or DemoStage.ReadyToMove)
            yield return new DemoStep(
                id,
                at + TimeSpan.FromMinutes(4),
                Note: new DemoNote("Loudness", $"Vocal - Full: gain set to reach -14 LUFS")
            );

        yield return new DemoStep(
            id,
            at + TimeSpan.FromMinutes(40),
            Action: new DemoAction(UserActionKind.GenreSettled, track.Genre)
        );
    }
}
