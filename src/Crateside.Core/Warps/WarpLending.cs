namespace Crateside.Core.Warps;

/// <summary>
/// Gives every staged stem without a finished warp the warp its siblings agree on, whether the
/// stem just landed or the user just saved a sibling (#54, ADR 0043).
/// </summary>
public sealed class WarpLending(CratesidePaths paths, WarpBorrower borrower)
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

            if (siblings.AuthoritiesFor(target) is not { Count: > 0 } authorities)
                continue;

            var signature = Signature(target.Path, authorities);

            if (_tried.TryGetValue(target.Path, out var tried) && tried == signature)
                continue;

            var outcome = await borrower.BorrowAsync(
                target.Path,
                authorities,
                siblings.SameKindAs(target),
                ct
            );

            // A failure is a locked or half-written file, so it is tried again next time.
            if (outcome is WarpBorrowOutcome.Failed)
                _tried.Remove(target.Path);
            else
                _tried[target.Path] = Signature(target.Path, authorities);

            loans.Add(new WarpLoan(target.Path, outcome));
        }

        return loans;
    }

    private static string Signature(string target, IReadOnlyList<string> authorities) =>
        string.Join(
            '|',
            [
                Stamp(target),
                Stamp($"{target}.asd"),
                .. authorities.Select(path => Stamp($"{path}.asd")),
            ]
        );

    private static string Stamp(string path) =>
        new FileInfo(path) is { Exists: true } file
            ? $"{file.Length}:{file.LastWriteTimeUtc.Ticks}"
            : "-";
}

public sealed record WarpLoan(string Target, WarpBorrowOutcome Outcome);

/// <summary>The <c>Warps</c> section of appsettings.</summary>
public sealed class WarpOptions
{
    public const string SectionName = "Warps";

    /// <summary>
    /// Whether staged stems take their siblings' finished warps without being asked. Off until the
    /// user has checked a borrowed warp in Live (ADR 0044).
    /// </summary>
    public bool LendAutomatically { get; set; }
}
