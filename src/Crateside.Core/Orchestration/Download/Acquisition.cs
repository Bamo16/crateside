namespace Crateside.Core.Orchestration.Download;

/// <summary>
/// How the download crateside is holding for one track turned out, kept beside the audio so it
/// dies with it.
/// </summary>
public sealed record Acquisition
{
    public PremiumStatus PremiumStatus { get; init; }

    /// <summary>Whether the format actually fetched was a premium one.</summary>
    public bool IsPremium { get; init; }

    /// <summary>
    /// The user has seen the shortfall of this download, not the track, and said to go on anyway.
    /// </summary>
    public bool IsAccepted { get; init; }

    /// <summary>
    /// Whether this download is a finding the user has to resolve before anything else runs.
    /// </summary>
    public bool Stops => !IsAccepted && IsAboutTheSource;

    /// <summary>
    /// What to tell a user choosing an upload, or null when there is nothing worth saying.
    /// </summary>
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
    /// The shortfall is about the upload, the only kind the user can act on one track at a time;
    /// an account problem is true of every download at once.
    /// </summary>
    private bool IsAboutTheSource =>
        PremiumStatus is PremiumStatus.NoPremiumLadder or PremiumStatus.SourceHasNoPremiumAudio;

    /// <summary>
    /// Reads what <c>stemforge-cli download --json</c> reported for one input; a name it does not
    /// know reads as <see cref="PremiumStatus.Unreported"/>.
    /// </summary>
    public static PremiumStatus StatusFrom(string? reported) =>
        Enum.TryParse<PremiumStatus>(reported, out var status) && Enum.IsDefined(status)
            ? status
            : PremiumStatus.Unreported;
}

/// <summary>
/// How an acquisition compared against the premium audio the user's cookies entitle them to:
/// StemForge's <c>PremiumStatus</c>, by the names it serializes.
/// </summary>
public enum PremiumStatus
{
    /// <summary>Nothing was reported — an older StemForge, or an input that never resolved.</summary>
    Unreported,

    /// <summary>No cookie source configured, or not a YouTube source. Nothing can be said.</summary>
    NotApplicable,

    /// <summary>The format fetched was premium. Nothing to report.</summary>
    Premium,

    /// <summary>
    /// A premium format was on offer and a different one was taken deliberately — never by crateside.
    /// </summary>
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
