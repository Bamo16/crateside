namespace Crateside.Core.Pairing;

/// <summary>
/// What a partner <em>is</em> to the source key, in musical terms — a description, not a cost.
/// </summary>
public enum Relationship
{
    /// <summary>The same key.</summary>
    PerfectMatch,

    /// <summary>The relative major or minor — same circle position, other mode.</summary>
    Relative,

    /// <summary>One step clockwise on the circle, same mode.</summary>
    EnergyRise,

    /// <summary>One step counter-clockwise on the circle, same mode.</summary>
    Relaxed,

    /// <summary>One step around the circle <em>and</em> a change of mode.</summary>
    MoodShift,

    /// <summary>Everything else not covered by the other values.</summary>
    Modulation,
}

public static class RelationshipExtensions
{
    public static string ToDisplayString(this Relationship relationship) =>
        relationship switch
        {
            Relationship.PerfectMatch => "Perfect Match",
            Relationship.Relative => "Relative (Epic)",
            Relationship.EnergyRise => "Energy Rise",
            Relationship.Relaxed => "Relaxed",
            Relationship.MoodShift => "Mood Shift",
            _ => "Modulation",
        };
}
