using Crateside.Core.Ledger;
using Crateside.Core.Orchestration.Verdict;
using Crateside.Core.Pairing;
using Crateside.Core.Tempo;
using Crateside.Core.Warps;

namespace Crateside.Components;

/// <summary>The warp view's "Judge from these bars" form: its lines, and what it asks the judge (#116).</summary>
/// <param name="inBars">Whether ranges are bars through the warp, or minutes and seconds without one.</param>
public sealed class BarForm(bool inBars)
{
    public bool InBars { get; } = inBars;

    public List<BarLine> Lines { get; } = [new()];

    /// <summary>What the user hears, as typed: <c>C♯ min</c>.</summary>
    public string Ear { get; set; } = string.Empty;

    public DiatonicMode? EarMode { get; set; }

    /// <summary>The form again with the ranges a finished run was given, for a second try.</summary>
    public static BarForm From(BarRun run, WarpGroup group)
    {
        var form = new BarForm(group.Lender is not null) { EarMode = run.Direction.Ear?.Mode };

        form.Lines.Clear();
        form.Lines.AddRange(
            run.Direction.Windows.Select(window => new BarLine
            {
                Range =
                    form.InBars && BarRanges.Show(group, window.Start, window.End) is { } bars
                        ? bars
                        : TimeText(window.Start, window.End),
                Label = window.Label ?? string.Empty,
            })
        );
        form.Ear = run.Direction.Ear?.Collapsed ?? string.Empty;

        if (run.Direction.Note is { Length: > 0 } note)
            form.Lines[0].Note = note;

        return form;
    }

    /// <summary>A line's range as a window, or null while it does not read as one.</summary>
    public RunWindow? Window(BarLine line, WarpGroup group) => Read(line, group);

    /// <summary>A line's resolved stretch, "bar 106 to the end (2:22–3:39)", so its meaning shows before judging.</summary>
    public string? Stretch(BarLine line, WarpGroup group) =>
        InBars && Window(line, group) is { } window
            ? $"{Where(window, group)}({TempoFormat.Clock(window.Start)}–{TempoFormat.Clock(window.End)})"
            : null;

    /// <summary>What the form does when run: judge the bars given, or the whole track again when none are.</summary>
    public string Title(WarpGroup group) =>
        Windows(group) is [] ? "Judge the whole track again" : "Judge from these bars";

    public IReadOnlyList<RunWindow> Windows(WarpGroup group) =>
        [.. Lines.Select(line => Read(line, group)).OfType<RunWindow>()];

    /// <summary>The line numbers whose range is filled in but does not read.</summary>
    public IReadOnlyList<int> Unreadable(WarpGroup group) =>
        [
            .. Lines
                .Select((line, index) => (line, index))
                .Where(pair =>
                    pair.line.Range.Trim() is { Length: > 0 } && Window(pair.line, group) is null
                )
                .Select(pair => pair.index + 1),
        ];

    /// <summary>The line numbers whose bar range stops where it starts, or less than a sixteenth on.</summary>
    public IReadOnlyList<int> Empty(WarpGroup group) =>
        [
            .. Lines
                .Select((line, index) => (line, index))
                .Where(pair =>
                    InBars && BarRanges.Read(pair.line.Range, group) is BarRangeReading.TooShort
                )
                .Select(pair => pair.index + 1),
        ];

    /// <summary>What the user hears, or null when they left it empty or it is not a key.</summary>
    public TonalCenter? EarCenter()
    {
        if (MusicKey.FromString(Ear.Trim()) is not { } key)
            return null;

        var tonic = key.SharpName.Split(' ')[0];
        var mode = EarMode ?? (key.Mode is Mode.Minor ? DiatonicMode.Aeolian : DiatonicMode.Ionian);

        return new TonalCenter(tonic, mode);
    }

    public bool EarIsUnreadable => Ear.Trim() is { Length: > 0 } && EarCenter() is null;

    /// <summary>The request, with the lines' notes as one direction: each under its window's name when there are several.</summary>
    public JudgeDirection Direction(WarpGroup group, string? audio)
    {
        var noted = Lines
            .Select(line => (Window: Window(line, group), Note: line.Note.Trim()))
            .Where(pair => pair is { Window: not null, Note.Length: > 0 })
            .ToList();
        var note = noted switch
        {
            [] => null,
            [var only] when Windows(group).Count is 1 => only.Note,
            _ => string.Join('\n', noted.Select(pair => $"{pair.Window!.Name}: {pair.Note}")),
        };

        return new JudgeDirection(Windows(group), note, EarCenter(), audio);
    }

    public void Add() => Lines.Add(new());

    public void Remove(BarLine line)
    {
        Lines.Remove(line);

        if (Lines is [])
            Lines.Add(new());
    }

    /// <summary>
    /// Puts a range dragged on the ruler, in bars (the end is where it stops) or whole seconds, on its line, or on the first
    /// empty line (else a new one) when <paramref name="line"/> is -1.
    /// </summary>
    public void Place(int line, int first, int last)
    {
        if (line < 0 || line >= Lines.Count)
        {
            line = Lines.FindIndex(each => each.Range.Trim() is { Length: 0 });

            if (line < 0)
            {
                Lines.Add(new());
                line = Lines.Count - 1;
            }
        }

        Lines[line].Range = Text(first, last);
    }

    private RunWindow? Read(BarLine line, WarpGroup group)
    {
        var label = line.Label.Trim() is { Length: > 0 } given ? given : null;

        if (InBars)
            return JudgeRanges.Window(group, line.Range, label);

        // A track with no stems has no length to check a range against.
        return
            JudgeRanges.TryParseTimes(line.Range, out var start, out var end)
            && (group.Seconds is 0 || end <= group.Seconds + 1)
            ? new RunWindow(
                start,
                group.Seconds is 0 ? end : Math.Min(end, group.Seconds),
                label,
                WindowAsker.User
            )
            : null;
    }

    private static string Where(RunWindow window, WarpGroup group)
    {
        if (group.BeatAt(window.Start) is not { } first || group.BeatAt(window.End) is not { } last)
            return string.Empty;

        var bars = group.Bars;
        var (start, end) = (BarRanges.At(bars, first), BarRanges.At(bars, last));
        var atEnd = group.Seconds > 0 && window.End >= group.Seconds;

        return window switch
        {
            _ when atEnd && window.Start <= 0 => "the whole track ",
            _ when atEnd => $"bar {start} to the end ",
            { Start: <= 0 } => $"the start to bar {end} ",
            _ when bars.IsBarLine(first) && Math.Abs(last - first - bars.BeatsPerBar) < 1e-4 =>
                $"bar {start} ",
            _ => $"bars {start}–{end} ",
        };
    }

    // A time range cannot be empty, as a single bar can, so it is a second at least.
    private string Text(int from, int to) =>
        InBars ? BarText(from, to) : TimeText(from, Math.Max(to, from + 1));

    // The end is a bar, where the selection stops (ADR 0085).
    private static string BarText(int first, int end) => $"{first}–{end}";

    private static string TimeText(double start, double end) =>
        $"{TempoFormat.Clock(start)}–{TempoFormat.Clock(end)}";
}

/// <summary>One range of the form, as typed.</summary>
public sealed class BarLine
{
    public string Range { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
}
