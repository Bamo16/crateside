using AbleKit.Analysis;
using Crateside.Core;
using Crateside.Core.Tempo;
using Crateside.Core.Warps;
using Humanizer;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace Crateside.Components;

/// <summary>The warp view's edit bar: a draft over the track's warp, drawn on the lanes until it is saved (handoff 24, ADR 0107).</summary>
public partial class WarpView
{
    private enum Phase
    {
        Idle,
        Saving,
        Undoing,
    }

    private enum Ask
    {
        None,
        Close,
        Judge,
    }

    private readonly Dictionary<string, WavePeaks> _sentPeaks =
    [
        with(StringComparer.OrdinalIgnoreCase),
    ];

    private bool _editing;
    private WarpDraft _draft = new();
    private WarpSavePlan? _offer;
    private WarpSavePlan.Ready? _plan;
    private WarpSiblings? _editSiblings;
    private Phase _phase;
    private Ask _asking;
    private int _written;
    private string? _notSaved;
    private string? _failed;
    private string? _line;
    private WarpSaved? _undo;
    private DateTimeOffset _lineAt;
    private string? _leaveTo;
    private bool _focusStay;
    private bool _focusKeep;
    private ElementReference _stay;
    private ElementReference _keep;
    private IJSObjectReference? _editJs;
    private WarpGroup? _drawnFrom;
    private WarpDraft? _drawnDraft;
    private WarpGroup? _drawn;
    private GridReport? _drawnOff;

    private bool IsDirty => _editing && !_draft.IsEmpty;

    private bool IsDrafting => IsDirty && _plan is not null && _drawn is not null;

    /// <summary>The group the page draws: the warp on disk, or with the draft applied to the stems a save would write.</summary>
    private WarpGroup Drawn(WarpGroup disk)
    {
        if (!IsDirty || _plan is not { } plan)
            return disk;

        if (
            ReferenceEquals(_drawnFrom, disk)
            && ReferenceEquals(_drawnDraft, _draft)
            && _drawn is { } kept
        )
            return kept;

        var draft = _draft;
        var touched = plan
            .Stems.Select(stem => stem.Path)
            .Concat(plan.Lent)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        StemWarp[] stems =
        [
            .. disk.Stems.Select(stem =>
                stem.Sidecar is { } file && touched.Contains(stem.Path)
                    ? stem with
                    {
                        Sidecar = WarpEdit.Apply(file, draft),
                    }
                    : stem
            ),
        ];
        var lender =
            stems.FirstOrDefault(stem => PathIs(stem.Path, plan.Reference))
            ?? stems.FirstOrDefault(stem => disk.Lender is { } was && PathIs(stem.Path, was.Path));

        (_drawnFrom, _drawnDraft) = (disk, draft);
        _drawn = new WarpGroup(stems, lender);
        _drawnOff = lender is null ? null : WarpTrueUp.Check(lender);

        return _drawn;
    }

    private static bool PathIs(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private bool IsSaving(StemWarp stem) =>
        _plan is { } plan
        && (
            plan.Stems.Any(held => PathIs(held.Path, stem.Path))
            || plan.Lent.Any(lent => PathIs(lent, stem.Path))
        );

    private bool Drafted(StemWarp stem) => IsDirty && IsSaving(stem);

    private WarpSavePlan OfferNow()
    {
        if (_group is not { Lender: { } lender })
            return new WarpSavePlan.NoWarp();

        var (plan, siblings) = PlanNow(lender.Path);
        _editSiblings = siblings;

        return plan;
    }

    private (WarpSavePlan Plan, WarpSiblings Siblings) PlanNow(string lender)
    {
        var siblings = WarpSiblings.Of(
            Row.Samples.Select(sample => sample.Path)
                .Where(sample => sample is { Length: > 0 })
                .Where(file => !file.EndsWith(".asd", StringComparison.OrdinalIgnoreCase))
        );

        return (
            WarpSiblings.Read(lender) is { } song
                ? Editor.Plan(siblings, song)
                : new WarpSavePlan.Failed("its tags do not say which upload it was cut from"),
            siblings
        );
    }

    private void OpenEdit()
    {
        if (_group is not { Lender: { } lender } || _phase is not Phase.Idle)
            return;

        var (plan, siblings) = PlanNow(lender.Path);

        if (plan is not WarpSavePlan.Ready ready)
        {
            _offer = plan;

            return;
        }

        (_form, _openWindow) = (null, null);
        (_plan, _editSiblings, _offer) = (ready, siblings, plan);
        (_editing, _draft, _asking, _notSaved, _failed) = (
            true,
            new WarpDraft(),
            Ask.None,
            null,
            null
        );
        (_line, _undo) = (null, null);
        _drawn = null;
    }

    private void RequestClose()
    {
        if (IsDirty)
        {
            (_asking, _focusKeep) = (Ask.Close, true);

            return;
        }

        CloseEdit();
    }

    private void CloseEdit()
    {
        (_editing, _draft, _asking, _notSaved, _failed) = (
            false,
            new WarpDraft(),
            Ask.None,
            null,
            null
        );
        (_plan, _drawn, _drawnFrom, _drawnDraft) = (null, null, null, null);
    }

    private void ResetEdit()
    {
        CloseEdit();
        (_line, _undo, _leaveTo, _phase) = (null, null, null, Phase.Idle);
        _sentPeaks.Clear();
    }

    private void KeepEditing() => _asking = Ask.None;

    private void CloseWithoutSaving()
    {
        var judge = _asking is Ask.Judge;

        CloseEdit();

        if (judge && _group is { } group)
            Open(group);
    }

    /// <summary>The judge opener: closes the edit bar first, asking about a draft before it.</summary>
    private void RequestJudge(WarpGroup group)
    {
        if (IsDirty)
        {
            (_asking, _focusKeep) = (Ask.Judge, true);

            return;
        }

        CloseEdit();
        Open(group);
    }

    private void Revert()
    {
        _draft = new WarpDraft();
        (_asking, _notSaved, _failed) = (Ask.None, null, null);

        if (_group is not { Lender: { } lender })
        {
            CloseEdit();

            return;
        }

        var (plan, siblings) = PlanNow(lender.Path);

        if (plan is WarpSavePlan.Ready ready)
            (_plan, _editSiblings) = (ready, siblings);
        else
        {
            _offer = plan;
            CloseEdit();
        }
    }

    private void Later(int beats) => _draft = _draft.Later(beats);

    private void Nudge(int sign, MouseEventArgs mouse) =>
        _draft = _draft.Nudged(sign * (mouse.ShiftKey ? 5 : 1));

    private void Scale(double factor, WarpGroup disk)
    {
        if (disk.Lender is not { Sidecar: { } file })
            return;

        _draft = _draft.Scaled(factor, WarpEdit.ClipStartBeat(file, _draft) + _draft.BarShift);
    }

    private async Task ClipAtPlayheadAsync(WarpGroup group)
    {
        if (_engine is null)
            return;

        var seconds = await _engine.InvokeAsync<double>("playhead");

        if (group.BeatAt(seconds) is { } beat)
            SetClip(group.Bars.BarAt(beat + 0.001));
    }

    private void SetClip(int bar)
    {
        // Back on the disk's own bar, the draft holds no clip start.
        var onDisk =
            _group is { Lender.Sidecar: { } file }
            && BarRuler.Of(file.Warp.TimeSignature) is var ruler
            && ruler.BarAt((file.DefaultClip?.Start ?? 0) + 0.001) == bar
            && _draft.BarShift is 0
            && Math.Abs(_draft.TempoFactor - 1) < 1e-9;

        _draft = onDisk ? _draft with { ClipStartBar = null } : _draft.WithClipStart(bar);
    }

    [JSInvokable]
    public async Task SetClipStartBar(int bar)
    {
        if (_group is not null)
            SetClip(bar);

        await InvokeAsync(StateHasChanged);
    }

    [JSInvokable]
    public async Task EditKey(string key, bool shift)
    {
        if (!_editing || _asking is not Ask.None || _phase is not Phase.Idle)
            return;

        _draft = key switch
        {
            "," => _draft.Later(-1),
            "." => _draft.Later(1),
            "ArrowLeft" => _draft.Nudged(shift ? -5 : -1),
            "ArrowRight" => _draft.Nudged(shift ? 5 : 1),
            _ => _draft,
        };

        await InvokeAsync(StateHasChanged);
    }

    private void NotchKey(KeyboardEventArgs key, WarpGroup group)
    {
        var step = key.Key switch
        {
            "ArrowLeft" => -1,
            "ArrowRight" => 1,
            _ => 0,
        };

        if (step is 0)
            return;

        SetClip(ClipBar(group) + step * (key.ShiftKey ? 4 : 1));
    }

    private static int ClipBar(WarpGroup group) => group.Bars.BarAt(group.ClipStartBeat + 0.001);

    /// <summary>The whole bars in the view as <c>fraction:bar</c> pairs, for the notch grip to snap to.</summary>
    private static string ClipSnaps(WarpGroup group, double from, double to)
    {
        if (group.BeatAt(from) is not { } first || group.BeatAt(to) is not { } last)
            return string.Empty;

        var ruler = group.Bars;
        List<string> snaps = [];

        for (var index = ruler.IndexAt(first); snaps.Count < 2_000; index++)
        {
            var beat = index * ruler.BeatsPerBar;

            if (beat > last)
                break;

            if (group.SecondsAt(beat) is { } at && X(at, from, to) is >= 0 and <= 100 and var x)
                snaps.Add($"{F(x / 100)}:{BarRuler.Number(index)}");
        }

        return string.Join(',', snaps);
    }

    private async Task SaveAsync()
    {
        if (
            _plan is not { } plan
            || _editSiblings is not { } siblings
            || _phase is not Phase.Idle
            || _notSaved is not null
        )
            return;

        var draft = _draft;

        (_phase, _written, _failed, _asking) = (Phase.Saving, 0, null, Ask.None);
        await InvokeAsync(StateHasChanged);

        var outcome = await Task.Run(() =>
            Editor.SaveAsync(
                plan,
                siblings,
                draft,
                done =>
                {
                    _written = done;
                    _ = InvokeAsync(StateHasChanged);
                }
            )
        );

        _phase = Phase.Idle;

        switch (outcome)
        {
            case WarpSaveOutcome.Saved(var save):
                CloseEdit();
                (_undo, _lineAt) = (save, save.At);
                _line = $"Saved to {"stem".ToQuantity(save.Stems.Count + save.Lent.Count)}";
                break;
            case WarpSaveOutcome.Changed(var stem):
                _notSaved = stem;
                break;
            case WarpSaveOutcome.Failed(var error):
                _failed = error;
                break;
        }

        Reload(force: true);
        await Tracks.EnsureFreshAsync(TimeSpan.Zero);
    }

    private async Task UndoAsync()
    {
        if (_undo is not { } save || _phase is not Phase.Idle)
            return;

        _phase = Phase.Undoing;
        await InvokeAsync(StateHasChanged);

        var outcome = await Task.Run(() => Editor.Undo(save));
        var count = "stem".ToQuantity(save.Stems.Count + save.Lent.Count, ShowQuantityAs.Words);

        _phase = Phase.Idle;
        _undo = null;
        _line = outcome switch
        {
            WarpUndoOutcome.Undone => $"Put back: the {count} are as they were before the save",
            WarpUndoOutcome.Changed(var stem) =>
                $"Not put back: {NameOf(stem)} was saved over in Live since, so nothing was changed",
            WarpUndoOutcome.Failed(var error) => $"Not put back: {error}",
            _ => null,
        };

        Reload(force: true);
        await Tracks.EnsureFreshAsync(TimeSpan.Zero);
    }

    private string Possessive(string path) =>
        NameOf(path) is var name && name.EndsWith('s') ? $"{name}'" : $"{name}'s";

    private string NotSavedText(string path) =>
        $"Live saved {Possessive(path)} warp since you started. Revert to load it, then redo the edits.";

    private int SavingCount => _plan is { } plan ? plan.Stems.Count + plan.Lent.Count : 0;

    private string SavingText =>
        _phase is Phase.Undoing
            ? $"Putting {"stem".ToQuantity(_undo is { } save ? save.Stems.Count + save.Lent.Count : 0)} back…"
            : $"Saving to {"stem".ToQuantity(SavingCount)} · backing each sidecar up, then writing";

    /// <summary>Re-plans when a stem the save writes changed on disk while the draft is open.</summary>
    private void WatchPlan()
    {
        if (!_editing || _phase is not Phase.Idle || _notSaved is not null || _plan is not { } plan)
            return;

        if (plan.Stems.FirstOrDefault(stamp => SidecarStamp.Of(stamp.Path) != stamp) is { } changed)
            _notSaved = changed.Path;
    }

    private Task OnLeavingAsync(LocationChangingContext context)
    {
        if (
            IsDirty
            && new Uri(Nav.ToAbsoluteUri(context.TargetLocation).GetLeftPart(UriPartial.Path))
                is var target
            && target != new Uri(Nav.ToAbsoluteUri(Nav.Uri).GetLeftPart(UriPartial.Path))
        )
        {
            context.PreventNavigation();
            (_leaveTo, _focusStay) = (context.TargetLocation, true);
        }

        return Task.CompletedTask;
    }

    private void LeaveKey(KeyboardEventArgs key)
    {
        if (key.Key is "Escape")
            Stay();
    }

    private string? DraftTempo(WarpGroup disk, WarpGroup drawn) =>
        IsDirty && disk.Lender is { Sidecar: { } was } && drawn.Lender is { Sidecar: { } now }
            ? WarpEditWording.DraftTempo(was, now, _draft)
            : null;

    private void Stay() => _leaveTo = null;

    private void LeaveAnyway()
    {
        var target = _leaveTo;

        ResetEdit();

        if (target is not null)
            Nav.NavigateTo(target);
    }

    private IEnumerable<(string Label, string Text, bool Changed)> Cells(
        WarpGroup disk,
        WarpGroup drawn
    )
    {
        if (
            disk.Lender is not { Sidecar: { } was } lender
            || drawn.Lender is not { Sidecar: { } now }
        )
            yield break;

        var starter = lender.Owner is WarpOwner.Starter;
        var bar = WarpEditWording.Bar(was, now, _draft, lender.Seconds, starter);
        var nudge = WarpEditWording.Nudge(_draft, starter);
        var tempo = WarpEditWording.Tempo(was, now, _draft);
        var clip = WarpEditWording.ClipStart(was, _draft);

        yield return ("Bar", bar.Text, bar.Changed);
        yield return ("Nudge", nudge.Text, nudge.Changed);
        yield return ("Tempo", tempo.Text, tempo.Changed);
        yield return ("Clip start", clip.Text, clip.Changed);
    }

    private IReadOnlyList<string> SavesToLines() =>
        _plan is { } plan
            ? WarpEditWording.SavesTo(
                [.. plan.Stems.Select(stem => NameOf(stem.Path))],
                [.. plan.KeepsOwn.Select(NameOf)],
                [.. plan.Lent.Select(NameOf)]
            )
            : [];

    /// <summary>What the edit bar is editing, in the lane chips' words.</summary>
    private string Editing(WarpGroup group, WarpWording wording) =>
        group.Lender is { } lender
            ? $"{wording.Chip(lender).Label} · {"stem".ToQuantity(SavingCount)}"
            : string.Empty;

    private object? PeaksSent(StemWarp stem)
    {
        var peaks = Peaks(stem);

        if (_sentPeaks.TryGetValue(stem.Path, out var sent) && ReferenceEquals(sent, peaks))
            return null;

        _sentPeaks[stem.Path] = peaks;

        return PeaksOf(stem);
    }
}
