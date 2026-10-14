namespace Crateside.Core.Warps;

/// <summary>
/// Gives every staged stem without a finished warp the warp its siblings agree on, whether the
/// stem just landed or the user just saved a sibling (#54, ADR 0043), or a starter warp at the
/// measured tempo when nothing of its upload is warped (#77, ADR 0047, ADR 0048).
/// </summary>
/// <param name="steadyBpm">crateside's own steady tempo for an audio file, or null before it is read or when it drifts.</param>
/// <param name="waiting">Stems left for Live to analyse, which nothing is written to meanwhile (#97).</param>
public sealed class WarpLending(
    CratesidePaths paths,
    SidecarWriter writer,
    WarpOptions options,
    Func<string, double?>? steadyBpm = null,
    LiveAnalyses? waiting = null
)
{
    private readonly Dictionary<string, string> _tried = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Tries each staged stem whose own files or siblings' sidecars changed since its last try.</summary>
    public async Task<IReadOnlyList<WarpLoan>> LendAsync(CancellationToken ct = default)
    {
        var siblings = WarpSiblings.Scan([paths.StagingFolder, paths.LibraryFolder]);
        List<WarpLoan> loans = [];

        foreach (var target in siblings.In(paths.StagingFolder))
        {
            ct.ThrowIfCancellationRequested();

            if (waiting?.Of(target.Path) is not null)
                continue;

            var authorities = siblings.AuthoritiesFor(target);
            var sameKind = siblings.SameKindAs(target);
            IReadOnlyList<string> lenders =
            [
                .. authorities.Union(sameKind, StringComparer.OrdinalIgnoreCase),
            ];
            var tempo = options.StarterWarps is not StarterWarps.Off
                ? TempoOf(siblings.CutsOf(target))
                : null;
            var bpm = tempo?.Bpm;
            var signature = Signature(target.Path, lenders, bpm);

            if (_tried.TryGetValue(target.Path, out var tried) && tried == signature)
                continue;

            var outcome = await OutcomeAsync(target.Path, authorities, sameKind, tempo, ct);

            // A failure is a locked or half-written file, so it is tried again next time.
            if (outcome is WarpOutcome.Failed)
                _tried.Remove(target.Path);
            else
                _tried[target.Path] = Signature(target.Path, lenders, bpm);

            if (outcome is not null)
                loans.Add(new WarpLoan(target.Path, outcome));
        }

        return loans;
    }

    /// <summary>The group's tempo and the stem it was read from, which also times every stem's marker.</summary>
    private (double Bpm, string From)? TempoOf(IEnumerable<SiblingFile> cuts) =>
        WarpSiblings
            .TempoSources(cuts)
            .Select(cut => (Bpm: steadyBpm?.Invoke(cut.Path), From: cut.Path))
            .FirstOrDefault(source => source.Bpm is not null)
            is { Bpm: { } bpm } source
            ? (bpm, source.From)
            : null;

    /// <summary>A lent warp when a sibling holds one, else a starter; null when what applies is switched off.</summary>
    private async Task<WarpOutcome?> OutcomeAsync(
        string target,
        IReadOnlyList<string> authorities,
        IReadOnlyList<string> sameKind,
        (double Bpm, string From)? tempo,
        CancellationToken ct
    )
    {
        if (writer.HasWarpToLend(authorities, sameKind))
            return options.LendAutomatically
                ? await writer.BorrowAsync(target, authorities, sameKind, ct)
                : null;

        return tempo is var (bpm, from)
            ? await writer.StartAsync(
                target,
                sameKind,
                bpm,
                ct,
                from,
                options.StarterWarps is StarterWarps.OnTheGrid
            )
            : null;
    }

    private static string Signature(string target, IReadOnlyList<string> lenders, double? bpm) =>
        string.Join(
            '|',
            [
                Stamp(target),
                Stamp($"{target}.asd"),
                .. lenders.Select(path => Stamp($"{path}.asd")),
                $"{bpm}",
            ]
        );

    private static string Stamp(string path) =>
        new FileInfo(path) is { Exists: true } file
            ? $"{file.Length}:{file.LastWriteTimeUtc.Ticks}"
            : "-";
}

public sealed record WarpLoan(string Target, WarpOutcome Outcome);

/// <summary>The <c>Warps</c> section of appsettings.</summary>
public sealed class WarpOptions
{
    public const string SectionName = "Warps";

    /// <summary>
    /// Whether staged stems take their siblings' finished warps without being asked. Off until the
    /// user has checked a borrowed warp in Live (ADR 0044).
    /// </summary>
    public bool LendAutomatically { get; set; }

    /// <summary>
    /// What stems of an upload nothing is warped on get (ADR 0047, ADR 0048, ADR 0054). Changed from
    /// Settings while crateside runs, so every reader looks again each time.
    /// </summary>
    public StarterWarps StarterWarps { get; set; } = StarterWarps.TempoOnly;
}

/// <summary>What crateside writes for a stem nothing of its upload is warped on.</summary>
public enum StarterWarps
{
    /// <summary>Nothing: the sidecar is left for Live to write, with Live's own transients.</summary>
    Off,

    /// <summary>One marker at the start of the audio, at crateside's own tempo reading.</summary>
    TempoOnly,

    /// <summary>The marker on a beat the transients place, and the clip starting on it (ADR 0054).</summary>
    OnTheGrid,
}
