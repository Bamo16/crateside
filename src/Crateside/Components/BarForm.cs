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
    private (int Line, bool End)? _dragging;

    // A new range is held here until the drag ends: adding its line sooner moves the ruler away
    // from under the pointer.
    private (int Anchor, int At)? _pending;

    public bool InBars { get; } = inBars;

    public List<BarLine> Lines { get; } = [new()];

    /// <summary>What the user hears, as typed: <c>C♯ min</c>.</summary>
    public string Ear { get; set; } = string.Empty;

    public DiatonicMode? EarMode { get; set; }

    public bool IsDragging => _dragging is not null || _pending is not null;

    /// <summary>The range being dragged out, before it has a line.</summary>
    public RunWindow? Pending(WarpGroup group) =>
        _pending is var (anchor, at)
            ? Window(
                new BarLine { Range = Text(Math.Min(anchor, at), Math.Max(anchor, at)) },
                group
            )
            : null;

    /// <summary>Whether a drag starting at this unit would move a range's edge.</summary>
    public bool IsEdge(int at) =>
        Lines.Any(line => Units(line) is var (first, last) && (at == first || at == last));

    /// <summary>The form again with the ranges a finished run was given, for a second try.</summary>
    public static BarForm From(BarRun run, bool inBars)
    {
        var form = new BarForm(inBars) { EarMode = run.Direction.Ear?.Mode };

        form.Lines.Clear();
        form.Lines.AddRange(
            run.Direction.Windows.Select(window => new BarLine
            {
                Range =
                    inBars && window.Bars is { } bars
                        ? BarText(bars.First, bars.Last)
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
    public RunWindow? Window(BarLine line, WarpGroup group)
    {
        var label = line.Label.Trim() is { Length: > 0 } given ? given : null;

        if (InBars)
            return JudgeRanges.TryParseBars(line.Range, out var bars)
                ? JudgeRanges.Window(group, bars, label)
                : null;

        return
            JudgeRanges.TryParseTimes(line.Range, out var start, out var end)
            && end <= group.Seconds + 1
            ? new RunWindow(start, Math.Min(end, group.Seconds), label, WindowAsker.User)
            : null;
    }

    public IReadOnlyList<RunWindow> Windows(WarpGroup group) =>
        [.. Lines.Select(line => Window(line, group)).OfType<RunWindow>()];

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

    /// <summary>What the user hears, or null when they left it empty or it is not a key.</summary>
    public TonalCentre? EarCentre()
    {
        if (MusicKey.FromString(Ear.Trim()) is not { } key)
            return null;

        var tonic = key.SharpName.Split(' ')[0];
        var mode = EarMode ?? (key.Mode is Mode.Minor ? DiatonicMode.Aeolian : DiatonicMode.Ionian);

        return new TonalCentre(tonic, mode);
    }

    public bool EarIsUnreadable => Ear.Trim() is { Length: > 0 } && EarCentre() is null;

    /// <summary>The request, with the lines' notes as one direction: each under its window's name when there are several.</summary>
    public JudgeDirection Direction(WarpGroup group, string audio)
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

        return new JudgeDirection(Windows(group), note, EarCentre(), audio);
    }

    public void Add() => Lines.Add(new());

    public void Remove(BarLine line)
    {
        Lines.Remove(line);

        if (Lines is [])
            Lines.Add(new());
    }

    /// <summary>Starts a drag at a bar, or a whole second without a warp: on a range's edge it moves that edge, elsewhere it adds a range.</summary>
    public void BeginDrag(int at)
    {
        for (var i = 0; i < Lines.Count; i++)
        {
            if (Units(Lines[i]) is not var (first, last))
                continue;

            if (at == first || at == last)
            {
                _dragging = (i, at == last);

                return;
            }
        }

        _pending = (at, at);
    }

    public void DragTo(int at)
    {
        if (_pending is var (anchor, _))
        {
            _pending = (anchor, at);

            return;
        }

        if (_dragging is not var (index, end) || Units(Lines[index]) is not var (first, last))
            return;

        var (from, to) = end ? (first, at) : (at, last);

        // Dragged past the fixed edge: the other edge is now the one held.
        if (to < from)
        {
            (from, to) = (to, from);
            _dragging = (index, !end);
        }

        Lines[index].Range = Text(from, to);
    }

    /// <summary>Ends a drag; a new range takes the first empty line, else a line of its own.</summary>
    public void EndDrag()
    {
        _dragging = null;

        if (_pending is not var (anchor, at))
            return;

        _pending = null;

        var empty = Lines.FindIndex(line => line.Range.Trim() is { Length: 0 });

        if (empty < 0)
        {
            Lines.Add(new());
            empty = Lines.Count - 1;
        }

        Lines[empty].Range = Text(Math.Min(anchor, at), Math.Max(anchor, at));
    }

    /// <summary>A line's range in drag units (bars, or whole seconds), whatever it was typed as.</summary>
    private (int First, int Last)? Units(BarLine line)
    {
        if (InBars)
            return JudgeRanges.TryParseBars(line.Range, out var bars)
                ? (bars.First, bars.Last)
                : null;

        return JudgeRanges.TryParseTimes(line.Range, out var start, out var end)
            ? ((int)Math.Round(start), (int)Math.Round(end))
            : null;
    }

    // A time range cannot be empty, as a single bar can, so it is a second at least.
    private string Text(int from, int to) =>
        InBars ? BarText(from, to) : TimeText(from, Math.Max(to, from + 1));

    private static string BarText(int first, int last) => $"{first}–{last}";

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
