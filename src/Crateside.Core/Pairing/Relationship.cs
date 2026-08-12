namespace Crateside.Core.Pairing;

/// <summary>
/// What a partner <em>is</em> to the source key, in musical terms — a description, not a cost.
/// </summary>
/// <remarks>
/// <para>
/// The cmdlet calls this "Vibe". CONTEXT.md resolves the term to <strong>relationship</strong>,
/// which is what this is named; the <em>values</em> are ported unchanged so behaviour matches
/// the cmdlet exactly.
/// </para>
/// <para>
/// <see cref="Modulation"/> is knowingly lossy — it is the catch-all for every relationship
/// that is not the tonic, the relative, or one step around the circle, so it covers musically
/// distinct borrowings and ends up on nearly everything once a shift is involved. Splitting it
/// is a deliberate behaviour change from the cmdlet, not a silent fix, and has not been made.
/// </para>
/// </remarks>
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

    /// <summary>Everything else. Lossy by inheritance — see the type remarks.</summary>
    Modulation,
}

public static class RelationshipExtensions
{
    /// <summary>The cmdlet's own label, kept so the two tools read the same.</summary>
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
