using Humanizer;

namespace Crateside.Core.Orchestration.Verdict;

/// <summary>What ordinary judging will pick from, as the dashboard's Judging card counts it.</summary>
public sealed record JudgingBacklog(int Intake, int Rejudges, int Backfill)
{
    public int Total => Intake + Rejudges + Backfill;

    /// <summary>True when more than intake is waiting, so the count needs words.</summary>
    public bool HasMoreThanIntake => Rejudges > 0 || Backfill > 0;

    /// <summary>"3 new, 50 re-judges, 8 backfill waiting", leaving out what is empty.</summary>
    public string Describe() => Total is 0 ? "0 waiting" : $"{string.Join(", ", Parts)} waiting";

    private IEnumerable<string> Parts
    {
        get
        {
            if (Intake > 0)
                yield return HasMoreThanIntake ? $"{Intake} new" : Intake.ToString();

            if (Rejudges > 0)
                yield return "re-judge".ToQuantity(Rejudges);

            if (Backfill > 0)
                yield return $"{Backfill} backfill";
        }
    }
}
