namespace Crateside.Core.Orchestration;

/// <summary>
/// The conversation judging is currently using, and when to start a fresh one.
/// </summary>
/// <remarks>
/// <para>
/// A verdict run's cost is dominated by what the session loads before it weighs anything — the
/// skill, its sources, the tooling. Paid once per track that is most of the bill; paid once per
/// <em>conversation</em> it is a rounding error, because every track after the first reads a warm
/// cache instead of building one.
/// </para>
/// <para>
/// <strong>Resuming rather than batching keeps the loop in crateside.</strong> Handing an agent
/// twenty tracks would amortise the same context, but it would also hand over the things that
/// make this recoverable: a job record per track, a pause that takes effect between tracks, and
/// strikes that belong to the track that earned them. Here each track is still its own
/// invocation and its own record — they simply share a conversation.
/// </para>
/// <para>
/// crateside mints the id rather than reading it back out of the session's output, so the
/// conversation is something it owns rather than something it tracks.
/// </para>
/// </remarks>
public sealed class JudgingSession(
    int maxTracks = JudgingSession.DefaultMaxTracks,
    long maxContextTokens = JudgingSession.DefaultMaxContextTokens
)
{
    /// <summary>How many tracks one conversation judges before a fresh one is started.</summary>
    public const int DefaultMaxTracks = 25;

    /// <summary>
    /// How large a conversation may get before it is retired, against a 200k window.
    /// </summary>
    /// <remarks>
    /// The headroom is not politeness: a track whose evidence is ambiguous can spend a lot on
    /// searching, and running out of room <em>mid-judgment</em> would waste the whole turn rather
    /// than merely ending the conversation.
    /// </remarks>
    public const long DefaultMaxContextTokens = 120_000;

    private readonly Lock _gate = new();

    private Guid _id = Guid.NewGuid();
    private bool _started;
    private int _tracks;

    /// <summary>Where the next track's judgment should go.</summary>
    public JudgingTurn Next()
    {
        lock (_gate)
            return new JudgingTurn { SessionId = _id, IsNew = !_started };
    }

    /// <summary>
    /// Records that a track was judged in this conversation, and retires it if that took it past
    /// either bound.
    /// </summary>
    /// <param name="contextTokens">
    /// How large the conversation now is, as the session reported it. Zero when that could not be
    /// read, in which case the track count is the only bound left — which is why there is one.
    /// </param>
    public void Completed(long contextTokens)
    {
        lock (_gate)
        {
            _started = true;
            _tracks++;

            if (_tracks >= maxTracks || contextTokens >= maxContextTokens)
                Reset();
        }
    }

    /// <summary>
    /// Throws the conversation away without using it further.
    /// </summary>
    /// <remarks>
    /// For anything that ended badly — an error, an exhausted allowance, a session that could not
    /// be resumed. Continuing a conversation whose last turn failed risks carrying whatever went
    /// wrong into every track after it, and starting again costs one context load.
    /// </remarks>
    public void Abandon()
    {
        lock (_gate)
            Reset();
    }

    private void Reset()
    {
        _id = Guid.NewGuid();
        _started = false;
        _tracks = 0;
    }
}

/// <summary>Which conversation one track's judgment belongs to.</summary>
public sealed record JudgingTurn
{
    public required Guid SessionId { get; init; }

    /// <summary>
    /// True when this conversation has not been used yet, so it is opened rather than resumed.
    /// </summary>
    public required bool IsNew { get; init; }
}
