using System.Collections.Immutable;

namespace Crateside.Core.Ledger;

/// <summary>A tonic and a diatonic mode: what the music is, before the collapse to a tag.</summary>
public sealed record TonalCentre(string Tonic, DiatonicMode Mode)
{
    /// <summary>The 12 tonics as the ledger spells them: sharps only, ASCII.</summary>
    public static readonly ImmutableArray<string> Tonics =
    [
        "C",
        "C#",
        "D",
        "D#",
        "E",
        "F",
        "F#",
        "G",
        "G#",
        "A",
        "A#",
        "B",
    ];

    /// <summary>The 24-key form Ableton is tagged with, keeping the tonic: <c>A minor</c> for A Dorian.</summary>
    public string Collapsed => $"{Tonic} {(IsMajor(Mode) ? "major" : "minor")}";

    /// <summary>Whether the collapse loses anything: a mode other than Ionian or Aeolian.</summary>
    public bool IsModal => Mode is not (DiatonicMode.Ionian or DiatonicMode.Aeolian);

    /// <summary><c>A Dorian</c>, or <c>A minor</c> where the mode is plain.</summary>
    public string Name => IsModal ? $"{Tonic} {Mode}" : Collapsed;

    /// <summary>A 24-key name (<c>Eb major</c>) as a centre on Ionian or Aeolian, or null.</summary>
    public static TonalCentre? FromKey(string? key) =>
        LedgerVocabulary.NormalizeKey(key)?.Split(' ') is [var tonic, var quality]
            ? new(tonic, quality is "major" ? DiatonicMode.Ionian : DiatonicMode.Aeolian)
            : null;

    /// <summary>Whether a mode collapses to major (Ionian, Lydian, Mixolydian) or to minor.</summary>
    public static bool IsMajor(DiatonicMode mode) =>
        mode is DiatonicMode.Ionian or DiatonicMode.Lydian or DiatonicMode.Mixolydian;
}

/// <summary>One of the seven rotations of the major scale.</summary>
public enum DiatonicMode
{
    Ionian,
    Dorian,
    Phrygian,
    Lydian,
    Mixolydian,
    Aeolian,
    Locrian,
}

/// <summary>A part of a song with its own tonal centre.</summary>
public sealed record Section(string Label, TonalCentre Centre);
