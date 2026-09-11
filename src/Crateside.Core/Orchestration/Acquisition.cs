namespace Crateside.Core.Orchestration;

/// <summary>
/// How the download crateside is holding for one track turned out.
/// </summary>
/// <remarks>
/// Kept beside the audio in the working set, so it dies with it: a swap or a hand-deleted working
/// set re-acquires, and the answer is measured again rather than inherited.
/// </remarks>
public sealed record Acquisition
{
    public PremiumStatus PremiumStatus { get; init; }

    /// <summary>Whether the format actually fetched was a premium one.</summary>
    public bool IsPremium { get; init; }

    /// <summary>
    /// The user has seen the shortfall and said to go on anyway.
    /// </summary>
    /// <remarks>
    /// Recorded here rather than with the standing instructions because it is about this
    /// download and not about the track — replacing the audio has to ask again.
    /// </remarks>
    public bool IsAccepted { get; init; }

    /// <summary>
    /// Whether this download is a finding the user has to resolve before anything else runs.
    /// </summary>
    public bool Stops => !IsAccepted && IsAboutTheSource;

    /// <summary>What to tell the user, or null when there is nothing worth saying.</summary>
    /// <remarks>
    /// Says what crateside can do about it, which is why it is not StemForge's wording for the
    /// same outcome: that advises a person choosing a format, this advises one choosing an upload.
    /// </remarks>
    public string? Advisory =>
        PremiumStatus switch
        {
            PremiumStatus.NoPremiumLadder =>
                "This is a video upload rather than a YouTube Music track, and no premium format "
                    + "was offered. If a song version exists, swapping to it gets better audio "
                    + "than anything separation can recover.",
            PremiumStatus.SourceHasNoPremiumAudio =>
                "This source offers nothing better than its freely-available formats. The "
                    + "session is signed in, so that is a property of the upload rather than "
                    + "anything wrong here.",
            _ => null,
        };

    /// <summary>
    /// The shortfall is about the upload, which is the only kind the user can act on one track
    /// at a time.
    /// </summary>
    /// <remarks>
    /// <see cref="PremiumStatus.NotSignedIn"/> and <see cref="PremiumStatus.AccountNotPremium"/>
    /// are deliberately absent: they are true of every download at once, so stopping one track
    /// for them would be the wrong shape of alarm.
    /// </remarks>
    private bool IsAboutTheSource =>
        PremiumStatus is PremiumStatus.NoPremiumLadder or PremiumStatus.SourceHasNoPremiumAudio;

    /// <summary>Reads what <c>stemforge-cli download --json</c> reported for one input.</summary>
    /// <remarks>
    /// An unrecognized name reads as <see cref="PremiumStatus.Unreported"/> rather than throwing,
    /// so a StemForge that names a new outcome cannot stop a download being recorded at all.
    /// </remarks>
    public static PremiumStatus StatusFrom(string? reported) =>
        Enum.TryParse<PremiumStatus>(reported, out var status) && Enum.IsDefined(status)
            ? status
            : PremiumStatus.Unreported;
}

/// <summary>
/// How an acquisition compared against the premium audio the user's cookies entitle them to.
/// </summary>
/// <remarks>
/// StemForge's <c>PremiumStatus</c>, by the names it serializes. It owns the reasoning; crateside
/// only decides what to do about each outcome.
/// </remarks>
public enum PremiumStatus
{
    /// <summary>Nothing was reported — an older StemForge, or an input that never resolved.</summary>
    Unreported,

    /// <summary>No cookie source configured, or not a YouTube source. Nothing can be said.</summary>
    NotApplicable,

    /// <summary>The format fetched was premium. Nothing to report.</summary>
    Premium,

    /// <summary>A premium format was on offer and a different one was taken deliberately.</summary>
    /// <remarks>crateside pins no format, so its own downloads never land here.</remarks>
    PremiumNotSelected,

    /// <summary>A music track, signed in, no premium offered — most likely the wrong profile.</summary>
    AccountNotPremium,

    /// <summary>A music track with no authentication evidence at all.</summary>
    NotSignedIn,

    /// <summary>Not a music track, signed in, and the source offers nothing better.</summary>
    SourceHasNoPremiumAudio,

    /// <summary>Not a music track and nothing gated at all — the ordinary video upload.</summary>
    NoPremiumLadder,
}
