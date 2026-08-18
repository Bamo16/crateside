using Crateside.Core.Orchestration;

namespace Crateside.Tests.Orchestration;

public class JudgingSessionTests
{
    [Fact]
    public void The_first_track_opens_a_conversation()
    {
        Assert.True(new JudgingSession().Next().IsNew);
    }

    [Fact]
    public void The_next_track_joins_the_one_before_it()
    {
        // The whole point: the skill and its sources are read once rather than per track.
        var session = new JudgingSession();
        var first = session.Next();

        session.Completed(contextTokens: 40_000);

        var second = session.Next();

        Assert.False(second.IsNew);
        Assert.Equal(first.SessionId, second.SessionId);
    }

    [Fact]
    public void A_conversation_is_retired_once_it_has_judged_enough()
    {
        var session = new JudgingSession(maxTracks: 3);
        var first = session.Next();

        for (var i = 0; i < 3; i++)
            session.Completed(contextTokens: 1_000);

        var next = session.Next();

        Assert.True(next.IsNew);
        Assert.NotEqual(first.SessionId, next.SessionId);
    }

    [Fact]
    public void A_conversation_is_retired_once_it_has_grown_too_large()
    {
        // Running out of room mid-judgment wastes the whole turn, so it is retired with headroom
        // rather than driven into the wall.
        var session = new JudgingSession(maxTracks: 100, maxContextTokens: 50_000);
        session.Next();

        session.Completed(contextTokens: 50_000);

        Assert.True(session.Next().IsNew);
    }

    [Fact]
    public void Room_to_spare_keeps_the_conversation()
    {
        var session = new JudgingSession(maxTracks: 100, maxContextTokens: 50_000);
        session.Next();

        session.Completed(contextTokens: 49_999);

        Assert.False(session.Next().IsNew);
    }

    [Fact]
    public void An_unreadable_usage_report_falls_back_to_counting_tracks()
    {
        // Zero means the report could not be read. The track count is the other bound, and it
        // exists precisely so this degrades into a smaller conversation rather than an endless
        // one.
        var session = new JudgingSession(maxTracks: 2, maxContextTokens: 50_000);
        session.Next();

        session.Completed(contextTokens: 0);
        Assert.False(session.Next().IsNew);

        session.Completed(contextTokens: 0);
        Assert.True(session.Next().IsNew);
    }

    [Fact]
    public void A_conversation_that_went_wrong_is_not_carried_forward()
    {
        // Whatever went wrong would otherwise sit in the history of every track after it, for
        // the sake of saving one context load.
        var session = new JudgingSession();
        var first = session.Next();

        session.Completed(contextTokens: 1_000);
        session.Abandon();

        var next = session.Next();

        Assert.True(next.IsNew);
        Assert.NotEqual(first.SessionId, next.SessionId);
    }

    [Fact]
    public void Asking_twice_without_finishing_gives_the_same_conversation()
    {
        // A track that was interrupted is retried, and it should rejoin rather than strand a
        // conversation nobody will ever speak into again.
        var session = new JudgingSession();

        Assert.Equal(session.Next().SessionId, session.Next().SessionId);
    }

    [Fact]
    public void One_tracks_own_work_does_not_retire_a_conversation()
    {
        // A real track created ~90k of cache against a 120k bound. Counting cache_read as well
        // -- a further 1,003,735, being the same context re-read once per turn -- put a single
        // track over a million and retired the conversation every time, which turned resuming
        // silently back into a fresh session per track.
        var session = new JudgingSession();
        session.Next();

        session.Completed(contextTokens: 89_932);

        Assert.False(session.Next().IsNew);
    }

    [Fact]
    public void The_defaults_are_the_agreed_ones()
    {
        Assert.Equal(25, JudgingSession.DefaultMaxTracks);
        Assert.Equal(120_000, JudgingSession.DefaultMaxContextTokens);
    }
}
