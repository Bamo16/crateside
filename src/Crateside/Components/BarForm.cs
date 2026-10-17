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

    /// <summary>What the form does when run: judge the bars given, or the whole track again when none are.</summary>
    public string Title(WarpGroup group) =>
        Windows(group) is [] ? "Judge the whole track again" : "Judge from these bars";

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

        return new JudgeDirection(Windows(group), note, EarCentre(), audio);
    }

    public void Add() => Lines.Add(new());

    public void Remove(BarLine line)
    {
        Lines.Remove(line);

        if (Lines is [])
            Lines.Add(new());
    }

    /// <summary>
    /// Puts a range dragged on the ruler, in bars or whole seconds, on its line, or on the first
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
