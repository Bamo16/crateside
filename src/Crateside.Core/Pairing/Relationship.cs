namespace Crateside.Core.Pairing;

/// <summary>What a partner is to the source, where it is one of the two worth naming (ADR 0056).</summary>
public enum Relationship
{
    SameKey,

    /// <summary>A key holding the source's seven notes on another tonic.</summary>
    SameNotes,
}
