using Crateside.Core.Orchestration.Loop;
using Crateside.Core.Quota;
using Microsoft.Extensions.Time.Testing;

namespace Crateside.Tests.Quota;

public sealed class SpendDownTests : IDisposable
{
    private static readonly DateTimeOffset Reset = new(2026, 10, 28, 9, 0, 0, TimeSpan.Zero);

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    private FakeTimeProvider _time = new(Reset - TimeSpan.FromHours(2));
    private readonly Usage _usage = new();
    private readonly Judge _judge = new();

    public SpendDownTests()
    {
        Directory.CreateDirectory(_directory);
        _judge.Usage = _usage;
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    private SpendDown Spend(bool enabled = true)
    {
        var paused = new PauseStore(Path.Combine(_directory, "paused.json"));

        paused.RememberBackfill(enabled);

        return new(
            paused,
            new SpendDownStore(Path.Combine(_directory, "spend-down.json")),
            _usage,
            _judge,
            _time
        );
    }

    private static async Task<int> RunUntilNothingAsync(SpendDown spend)
    {
        var passes = 0;

        while (
            passes < 100
            && await spend.RunOnceAsync(TestContext.Current.CancellationToken) is not null
        )
            passes++;

        return passes;
    }

    [Fact]
    public async Task Outside_the_window_nothing_is_judged()
    {
        _time = new(Reset - TimeSpan.FromHours(4));
        var spend = Spend();

        Assert.Null(await spend.RunOnceAsync(TestContext.Current.CancellationToken));
        Assert.Equal(0, _judge.Calls);
        Assert.Equal(new SpendDownState.Waiting(Reset - TimeSpan.FromHours(3), Reset), spend.State);
    }

    [Fact]
    public async Task Inside_the_window_it_judges_until_the_weekly_ceiling_and_then_stops()
    {
        _usage.Weekly = 95;
        var spend = Spend();

        var passes = await RunUntilNothingAsync(spend);

        Assert.Equal(5, passes);
        Assert.Equal(100, _usage.Weekly);
        Assert.Equal(new SpendDownState.Spent(100, Reset), spend.State);
    }

    [Fact]
    public async Task Each_pass_reports_weekly_use_before_and_after_its_track()
    {
        _usage.Weekly = 60;

        var pass = await Spend().RunOnceAsync(TestContext.Current.CancellationToken);

        Assert.Equal((60, 61, 1), (pass?.WeeklyBefore, pass?.WeeklyAfter, pass?.Delta));
        Assert.Equal("Track 1", pass?.Tick.Subject);
    }

    [Fact]
    public async Task A_weekly_reading_at_the_ceiling_judges_nothing()
    {
        _usage.Weekly = 100;

        Assert.Null(await Spend().RunOnceAsync(TestContext.Current.CancellationToken));
        Assert.Equal(0, _judge.Calls);
    }

    [Fact]
    public async Task A_lower_ceiling_from_settings_stops_it_sooner()
    {
        _usage.Weekly = 80;
        var spend = Spend();
        spend.Remember(spend.Settings with { WeeklyCeiling = 82 });

        Assert.Equal(2, await RunUntilNothingAsync(spend));
    }

    [Fact]
    public async Task A_five_hour_window_near_its_limit_holds_the_spend_down()
    {
        _usage.Weekly = 50;
        _usage.Session = 100;
        var spend = Spend();

        Assert.Null(await spend.RunOnceAsync(TestContext.Current.CancellationToken));
        Assert.Equal(0, _judge.Calls);
        Assert.IsType<SpendDownState.SessionFull>(spend.State);
    }

    [Fact]
    public async Task The_five_hour_ceiling_stops_a_run_that_climbs_into_it()
    {
        _usage.Weekly = 50;
        _usage.Session = 85;
        _judge.SessionPerTrack = 3;
        var spend = Spend();
        spend.Remember(spend.Settings with { SessionCeiling = 90 });

        Assert.Equal(2, await RunUntilNothingAsync(spend));
        Assert.Equal(91, _usage.Session);
    }

    [Fact]
    public async Task The_window_follows_the_reset_the_usage_source_reports()
    {
        var later = Reset + TimeSpan.FromDays(1.5);
        _usage.ResetsAt = later;
        _usage.Weekly = 50;
        var spend = Spend();

        Assert.Null(await spend.RunOnceAsync(TestContext.Current.CancellationToken));

        _time.SetUtcNow(later - TimeSpan.FromHours(1));

        Assert.NotNull(await spend.RunOnceAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, _judge.Calls);
    }

    [Fact]
    public async Task A_longer_window_from_settings_opens_it_sooner()
    {
        _time = new(Reset - TimeSpan.FromHours(20));
        _usage.Weekly = 50;
        var spend = Spend();
        spend.Remember(spend.Settings with { WindowHours = 24 });

        Assert.NotNull(await spend.RunOnceAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task An_unread_quota_judges_nothing()
    {
        _usage.Readable = false;
        var spend = Spend();

        Assert.Null(await spend.RunOnceAsync(TestContext.Current.CancellationToken));
        Assert.Equal(0, _judge.Calls);
        Assert.IsType<SpendDownState.Unread>(spend.State);
    }

    [Fact]
    public async Task Turned_off_it_judges_nothing_and_reads_nothing()
    {
        var spend = Spend(enabled: false);

        Assert.Null(await spend.RunOnceAsync(TestContext.Current.CancellationToken));
        Assert.Equal((0, 0), (_judge.Calls, _usage.Reads));
        Assert.IsType<SpendDownState.Off>(spend.State);
    }

    [Fact]
    public void The_switch_and_the_settings_survive_a_restart()
    {
        var spend = Spend(enabled: false);
        spend.SetEnabled(true);
        spend.Remember(
            new SpendDownSettings
            {
                WindowHours = 9,
                WeeklyCeiling = 95,
                SessionCeiling = 70,
            }
        );

        var restarted = new SpendDown(
            new PauseStore(Path.Combine(_directory, "paused.json")),
            new SpendDownStore(Path.Combine(_directory, "spend-down.json")),
            _usage,
            _judge,
            _time
        );

        Assert.True(restarted.IsEnabled);
        Assert.Equal(
            new SpendDownSettings
            {
                WindowHours = 9,
                WeeklyCeiling = 95,
                SessionCeiling = 70,
            },
            restarted.Settings
        );
    }

    [Fact]
    public void Settings_out_of_range_are_kept_in_range()
    {
        var store = new SpendDownStore(Path.Combine(_directory, "spend-down.json"));

        store.Remember(
            new SpendDownSettings
            {
                WindowHours = 0,
                WeeklyCeiling = 150,
                SessionCeiling = -5,
            }
        );

        Assert.Equal(
            new SpendDownSettings
            {
                WindowHours = 1,
                WeeklyCeiling = 100,
                SessionCeiling = 1,
            },
            store.Read()
        );
    }

    [Fact]
    public async Task A_track_cut_off_by_the_limit_holds_the_spend_down_until_the_five_hour_window_rolls()
    {
        _judge.Unavailable = true;
        var spend = Spend();

        var cutOff = await spend.RunOnceAsync(TestContext.Current.CancellationToken);

        Assert.True(cutOff?.Tick.IsAgentUnavailable);
        Assert.Equal(new SpendDownState.CutOff(Reset - TimeSpan.FromHours(1)), spend.State);

        _time.Advance(TimeSpan.FromMinutes(30));
        _judge.Unavailable = false;

        Assert.Null(await spend.RunOnceAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, _judge.Calls);

        _time.SetUtcNow(Reset - TimeSpan.FromMinutes(59));

        Assert.NotNull(await spend.RunOnceAsync(TestContext.Current.CancellationToken));
        Assert.Equal(2, _judge.Calls);
    }

    private sealed class Usage : IQuotaReader
    {
        public double Weekly { get; set; } = 50;
        public double Session { get; set; } = 20;
        public DateTimeOffset ResetsAt { get; set; } = Reset;
        public bool Readable { get; set; } = true;
        public int Reads { get; private set; }

        public Task<QuotaSnapshot> ReadAsync(CancellationToken ct = default)
        {
            Reads++;

            return Task.FromResult(
                Readable
                    ? new QuotaSnapshot
                    {
                        Utilization = Session,
                        ResetsAt = Reset - TimeSpan.FromHours(1),
                        WeeklyUtilization = Weekly,
                        WeeklyResetsAt = ResetsAt,
                    }
                    : QuotaSnapshot.Unknown
            );
        }
    }

    /// <summary>Spends one weekly percent per track, and a set amount of the five-hour window.</summary>
    private sealed class Judge : IBackfillJudge
    {
        public Usage Usage { get; set; } = null!;
        public double SessionPerTrack { get; set; }
        public bool Unavailable { get; set; }
        public int Calls { get; private set; }

        public Task<StageTick> JudgeBackfillAsync(CancellationToken ct = default)
        {
            Calls++;

            if (Unavailable)
                return Task.FromResult(
                    new StageTick
                    {
                        Outcome = StageOutcome.Ran,
                        Subject = $"Track {Calls}",
                        IsAgentUnavailable = true,
                        Error = "the Claude allowance is spent",
                    }
                );

            Usage.Weekly += 1;
            Usage.Session += SessionPerTrack;

            return Task.FromResult(
                new StageTick
                {
                    Outcome = StageOutcome.Ran,
                    Subject = $"Track {Calls}",
                    IsSuccess = true,
                }
            );
        }
    }
}
