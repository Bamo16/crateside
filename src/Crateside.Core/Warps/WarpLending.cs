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
        var siblings = WarpSiblings.Scan([paths.SampleStaging, paths.MashupSamples]);
        List<WarpLoan> loans = [];

        foreach (var target in siblings.In(paths.SampleStaging))
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
            var bpm = options.WriteStarterWarps ? TempoOf(siblings.CutsOf(target)) : null;
            var signature = Signature(target.Path, lenders, bpm);

            if (_tried.TryGetValue(target.Path, out var tried) && tried == signature)
                continue;

            var outcome = await OutcomeAsync(target.Path, authorities, sameKind, bpm, ct);

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

    private double? TempoOf(IEnumerable<SiblingFile> cuts) =>
        WarpSiblings
            .TempoSources(cuts)
            .Select(cut => steadyBpm?.Invoke(cut.Path))
            .FirstOrDefault(bpm => bpm is not null);

    /// <summary>A lent warp when a sibling holds one, else a starter; null when what applies is switched off.</summary>
    private async Task<WarpOutcome?> OutcomeAsync(
        string target,
        IReadOnlyList<string> authorities,
        IReadOnlyList<string> sameKind,
        double? bpm,
        CancellationToken ct
    )
    {
        if (writer.HasWarpToLend(authorities, sameKind))
            return options.LendAutomatically
                ? await writer.BorrowAsync(target, authorities, sameKind, ct)
                : null;

        return bpm is { } tempo ? await writer.StartAsync(target, sameKind, tempo, ct) : null;
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
    /// Whether stems of an upload nothing is warped on get a starter warp at crateside's own tempo
    /// reading. Off until the user has checked a borrowed warp in Live (ADR 0047, ADR 0048).
    /// </summary>
    public bool WriteStarterWarps { get; set; }
}
