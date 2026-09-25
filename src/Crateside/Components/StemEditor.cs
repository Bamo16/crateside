using Crateside.Core;
using Crateside.Core.Orchestration;
using Crateside.Core.Queue;
using Crateside.Core.Staging;
using Crateside.Core.Tracks;
using Humanizer;

namespace Crateside.Components;

/// <summary>
/// The picker's state and its edits, shared by the list and the confirm wherever a page mounts
/// them, so the row and Track details cannot drift apart (#21).
/// </summary>
public sealed class StemEditor(
    QueueService playlist,
    StemRecut recut,
    SourceAdd sources,
    SampleAdoption adoption,
    StageSignals signals,
    TracksService tracks,
    PresetCatalogReader presetReader,
    ActionLog actions
) : IDisposable
{
    public const string AdoptNote =
        "crateside writes this file's provenance once, where it is, and the track keeps it. "
        + "Nothing can make it again, so dropping it from the plan later deletes it.";

    private readonly CancellationTokenSource _circuit = new();

    /// <summary>
    /// Plans ticked here that the rows have not caught up with yet, so a tick shows at once and the
    /// next one builds on it rather than on a plan a rescan has still to bring back.
    /// </summary>
    private readonly Dictionary<string, StemPlan> _ticked = new(StringComparer.Ordinal);

    private StemPlan _wanted = StemPlan.Default;

    /// <summary>The user picked the type, so the title no longer gets a say.</summary>
    private bool _addTypeChosen;

    /// <summary>The label crateside last filled in, which it may replace; one the user typed it may not.</summary>
    private string? _addLabelOffered;

    /// <summary>The video whose title was last asked for.</summary>
    private string? _titleOf;

    private string? _addTitle;
    private bool _readingTitle;

    /// <summary>Raised after every change, on whatever thread made it.</summary>
    public event Action? Changed;

    /// <summary>Raised when the picker opens, so the page can close whatever else was open.</summary>
    public event Action? Opened;

    public bool Working { get; private set; }

    public string? Problem { get; private set; }

    /// <summary>What the separator can make. Empty until loaded, or when it could not be asked.</summary>
    public PresetCatalog Presets { get; private set; } = PresetCatalog.Empty;

    /// <summary>The track whose picker is open, or null when none is.</summary>
    public string? Picking { get; private set; }

    public RecutPlan? Plan { get; private set; }

    /// <summary>The track whose add-a-source form is open, or null when none is.</summary>
    public string? Adding { get; private set; }

    public string AddUrl { get; private set; } = string.Empty;
    public string AddLabel { get; private set; } = string.Empty;
    public AddPlan? AddPlan { get; private set; }
    public SampleType? AddType { get; private set; }

    /// <summary>What the upload offers, once it has been asked and found wanting.</summary>
    public ProbedSource? Shortfall { get; private set; }

    /// <summary>The track whose adopt-a-file form is open, or null when none is.</summary>
    public string? Adopting { get; private set; }

    public IReadOnlyList<string> AdoptCandidates { get; private set; } = [];
    public string? AdoptPath { get; private set; }
    public SampleType? AdoptType { get; private set; }
    public AdoptPlan? AdoptPlan { get; private set; }

    public string? TitleNote =>
        _readingTitle ? "Reading the upload's title…"
        : _addTitle is not { } title ? null
        : _addTypeChosen || AddType is null ? $"“{title}”"
        : $"“{title}”, so {AddType} is suggested";

    public async Task LoadAsync() => Presets = await presetReader.ReadAsync(_circuit.Token);

    public bool IsPicking(TrackRow row) => Picking == row.VideoId;

    /// <summary>A tick has moved, so the page shows the confirm instead of what happens next.</summary>
    public bool IsConfirming(TrackRow row) => IsPicking(row) && Plan is not null;

    /// <summary>The track's files as lines to tick, against the plan being edited or its own.</summary>
    public StemPicker PickerFor(TrackRow row) =>
        StemPicker.For(row, Presets, IsPicking(row) ? _wanted : PlanOf(row));

    /// <summary>Closes everything, as when the page moves to another track.</summary>
    public void Reset()
    {
        Problem = null;
        CancelPicking();
    }

    /// <summary>Opens the picker, or shuts it again. Looking is not the start of anything.</summary>
    public void TogglePicking(TrackRow row)
    {
        Problem = null;

        if (IsPicking(row))
        {
            CancelPicking();
            return;
        }

        Picking = row.VideoId;
        _wanted = row.Plan;
        Plan = null;

        Opened?.Invoke();
        Changed?.Invoke();
    }

    public void CancelPicking()
    {
        Picking = null;
        _wanted = StemPlan.Default;
        Plan = null;

        CancelAdding();
        CancelAdopting();
    }

    /// <summary>
    /// Ticks one line: recorded at once before separation, only proposed after it, when the
    /// confirm is what spends anything.
    /// </summary>
    public void Toggle(TrackRow row, StemChoice line)
    {
        if (line.IsPinned)
            return;

        if (row.IsCutInFlight)
        {
            ToggleInFlight(row, line);
            Changed?.Invoke();

            return;
        }

        var plan = IsPicking(row) ? _wanted : PlanOf(row);

        var wanted = line switch
        {
            // Unticking an added source drops it from the plan; there is nothing to re-fetch,
            // so it is never ticked back on from here.
            { AddedSource: { } added } => plan.Dropping(added.Label),
            { Kind: StemKind.Source } => plan.TogglingSource(),
            _ => plan.Toggling(line.PresetId!),
        };

        if (row.CanChangePlan)
        {
            // Nothing has been made, so there is nothing to confirm and nothing to lose.
            playlist.SetPlan(row.VideoId, wanted);
            actions.Record(row.VideoId, UserActionKind.PlanChanged, line.Label);

            _ticked[row.VideoId] = wanted;

            if (IsPicking(row))
                _wanted = wanted;

            // Producing what was just ticked is a copy or a fetch, not a wait: without this the
            // stage is part-way through a two-minute idle delay and would not look again (#48).
            signals.Wake(JobStage.Download, JobStage.Separation);

            RefreshSoon();
            Changed?.Invoke();

            return;
        }

        _wanted = wanted;

        // A short track has something to confirm even when the plan is unchanged: making up the
        // shortfall is a re-cut of its own (#38).
        Plan =
            wanted.AsksTheSameAs(row.Plan) && !row.IsShort
                ? null
                : recut.PlanFor(row.VideoId, wanted, Presets, row.Inventory);

        Changed?.Invoke();
    }

    public Task ConfirmRecutAsync(TrackRow row) =>
        WorkAsync(async () =>
        {
            var picker = PickerFor(row);
            var outcome = recut.Apply(row.VideoId, _wanted, Presets, row.Inventory);

            Problem = outcome.Problem;

            if (outcome.Succeeded)
            {
                actions.Record(
                    row.VideoId,
                    UserActionKind.Recut,
                    $"{picker.Adding} new, {picker.Removing} removed",
                    picker.Adding > 0 ? JobStage.Separation : null
                );
                CancelPicking();
            }

            // A worker part-way through its two-minute idle wait would not look again.
            signals.Wake(JobStage.Download, JobStage.Separation);

            await tracks.RefreshAsync(_circuit.Token);
        });

    /// <summary>Makes what the plan already asks for and disk lacks, leaving the plan as it is.</summary>
    public Task CutShortfallAsync(TrackRow row) =>
        WorkAsync(async () =>
        {
            Problem = recut.Apply(row.VideoId, row.Plan, Presets, row.Inventory).Problem;

            if (Problem is null)
                actions.Record(
                    row.VideoId,
                    UserActionKind.ShortfallCut,
                    Missing(row),
                    JobStage.Separation
                );

            signals.Wake(JobStage.Separation);

            await tracks.RefreshAsync(_circuit.Token);
        });

    public string Missing(TrackRow row) =>
        string.Join(", ", row.Shortfall.Presets.Select(id => Presets.ById(id)?.DisplayName ?? id));

    public void StartAdding(TrackRow row)
    {
        CancelAdding();
        CancelAdopting();

        Problem = null;
        Adding = row.VideoId;
        Changed?.Invoke();
    }

    public void CancelAdding()
    {
        Adding = null;
        AddUrl = string.Empty;
        AddLabel = string.Empty;
        AddPlan = null;
        Shortfall = null;
        AddType = null;
        _addTypeChosen = false;
        _addLabelOffered = null;
        _titleOf = null;
        _addTitle = null;
        _readingTitle = false;
        Changed?.Invoke();
    }

    /// <summary>Re-reads what adding would do on every keystroke, so the refusal arrives early.</summary>
    public void OnAdd(TrackRow row, string? url = null, string? label = null)
    {
        AddUrl = url ?? AddUrl;
        AddLabel = label ?? AddLabel;
        Shortfall = null;

        if (YouTubeUrl.TryGetVideoId(AddUrl, out var videoId) && videoId != _titleOf)
            _ = ReadTitleAsync(row, videoId);

        Replan(row);
        Changed?.Invoke();
    }

    public void ChooseType(TrackRow row, SampleType type)
    {
        _addTypeChosen = true;
        Offer(type);
        Replan(row);
        Changed?.Invoke();
    }

    /// <summary>
    /// Adds the source, or comes back with what the upload actually offers before anything is
    /// fetched (ADR 0019).
    /// </summary>
    public Task ConfirmAddAsync(TrackRow row, bool anyway = false) =>
        WorkAsync(async () =>
        {
            var outcome = await sources.ApplyAsync(
                row.Facts,
                AddUrl,
                AddLabel,
                AddType,
                anyway,
                _circuit.Token
            );

            Problem = outcome.Problem;
            Shortfall = outcome.Shortfall;

            if (outcome.Succeeded)
            {
                actions.Record(
                    row.VideoId,
                    UserActionKind.SourceAdded,
                    $"{AddLabel} · {AddUrl}",
                    JobStage.Download
                );
                CancelPicking();
                signals.Wake(JobStage.Download);
            }

            await tracks.RefreshAsync(_circuit.Token);
        });

    public void StartAdopting(TrackRow row)
    {
        CancelAdding();
        CancelAdopting();

        Problem = null;
        Adopting = row.VideoId;
        AdoptCandidates = adoption.Candidates();
        Changed?.Invoke();
    }

    public void CancelAdopting()
    {
        Adopting = null;
        AdoptCandidates = [];
        AdoptPath = null;
        AdoptType = null;
        AdoptPlan = null;
        Changed?.Invoke();
    }

    public void OnAdopt(TrackRow row, string? path)
    {
        AdoptPath = path is { Length: > 0 } ? path : null;
        AdoptType = SampleTypes.InferredFrom(StemFileName.PresetOf(AdoptPath ?? string.Empty));
        AdoptPlan = adoption.PlanFor(row.Facts, AdoptPath, AdoptType);
        Changed?.Invoke();
    }

    public void ChooseAdoptType(TrackRow row, SampleType type)
    {
        AdoptType = type;
        AdoptPlan = adoption.PlanFor(row.Facts, AdoptPath, AdoptType);
        Changed?.Invoke();
    }

    public Task ConfirmAdoptAsync(TrackRow row) =>
        WorkAsync(async () =>
        {
            Problem = adoption.Apply(row.Facts, AdoptPath, AdoptType);

            if (Problem is null)
            {
                actions.Record(
                    row.VideoId,
                    UserActionKind.SampleAdopted,
                    Path.GetFileName(AdoptPath)
                );
                CancelAdopting();
            }

            await tracks.RefreshAsync(_circuit.Token);
        });

    /// <summary>What an added source is, said where the boxes to describe one are.</summary>
    public static string AddNote(TrackRow row) =>
        "Another file of this song, kept whole and never separated. keytag records it as this "
        + "song so it stages beside the stems"
        + (
            row.Key is { } key
                ? $" and takes the key, {KeyText.Spell(key)}."
                : ", and takes the key once this track has one."
        )
        + " No quota, no GPU.";

    /// <summary>What the GPU is being asked for, or nothing where it is not being asked.</summary>
    public static string Cost(SeparationRecipe recipe) =>
        recipe.Passes is 0
            ? string.Empty
            : $"{"pass".ToQuantity(recipe.Passes)} on the GPU, about {recipe.Estimate.Humanize()}.";

    public void Dispose()
    {
        _circuit.Cancel();
        _circuit.Dispose();
    }

    private StemPlan PlanOf(TrackRow row)
    {
        if (!_ticked.TryGetValue(row.VideoId, out var ticked))
            return row.Plan;

        if (!ticked.AsksTheSameAs(row.Plan))
            return ticked;

        _ticked.Remove(row.VideoId);

        return row.Plan;
    }

    /// <summary>
    /// Records a tick on a track already in a cut and makes nothing; an untick waits, since the
    /// separator may be writing that very file (ADR 0027).
    /// </summary>
    private void ToggleInFlight(TrackRow row, StemChoice line)
    {
        Problem = null;

        if (line.IsWanted)
        {
            Problem = "crateside is cutting this track. You can drop files once it has finished.";

            return;
        }

        var wanted = line switch
        {
            { Kind: StemKind.Source } => PlanOf(row).TogglingSource(),
            _ => PlanOf(row).Toggling(line.PresetId!),
        };

        playlist.SetPlan(row.VideoId, wanted);

        _ticked[row.VideoId] = wanted;
        _wanted = wanted;
        Plan = null;

        // A kept source is a copy the download stage makes whatever the GPU is doing.
        signals.Wake(JobStage.Download);

        RefreshSoon();
    }

    /// <summary>Rebuilds the rows without holding up the click that asked for it.</summary>
    private void RefreshSoon() =>
        _ = Task.Run(async () =>
        {
            try
            {
                await tracks.RefreshAsync(_circuit.Token);
            }
            catch (OperationCanceledException) { }
        });

    /// <summary>Suggests a type from what the upload is called, until the user has picked one.</summary>
    private async Task ReadTitleAsync(TrackRow row, string videoId)
    {
        _titleOf = videoId;
        _addTitle = null;
        _readingTitle = true;

        if (!_addTypeChosen)
            AddType = null;

        string? title;

        try
        {
            title = await sources.TitleOfAsync(videoId, _circuit.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        // The URL moved on while this was being asked.
        if (_titleOf != videoId || Adding != row.VideoId)
            return;

        _readingTitle = false;
        _addTitle = title;

        if (!_addTypeChosen && SampleTypes.InferredFrom(title) is { } inferred)
            Offer(inferred);

        Replan(row);
        Changed?.Invoke();
    }

    private void Offer(SampleType type)
    {
        AddType = type;

        if (AddLabel.Length > 0 && AddLabel != _addLabelOffered)
            return;

        AddLabel = SampleTypes.LabelFor(type);
        _addLabelOffered = AddLabel;
    }

    private void Replan(TrackRow row) =>
        AddPlan = sources.PlanFor(row.Facts, AddUrl, AddLabel, AddType);

    private async Task WorkAsync(Func<Task> work)
    {
        Working = true;
        Problem = null;
        Changed?.Invoke();

        try
        {
            await work();
        }
        catch (OperationCanceledException) { }
        finally
        {
            Working = false;
            Changed?.Invoke();
        }
    }
}
