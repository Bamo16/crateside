using AbleKit.Sets;

namespace Crateside.Core.Ableton;

/// <summary>The user's sets as a rename sees them: which use a file, which is open, and relinking.</summary>
public interface ISetLinks
{
    IReadOnlyList<string> Using(IReadOnlyCollection<string> samples);

    OpenSet ReadOpenSet();

    SetRelink Relink(IReadOnlyList<SampleMove> moves);
}

/// <summary>AbleKit's <see cref="LiveSets"/>, with sets named as the user sees them (ADR 0040).</summary>
public sealed class AbletonSetLinks(CratesidePaths paths, TimeProvider time) : ISetLinks
{
    private const string BackupLabel = "crateside";

    private readonly LiveSets _sets = new(paths.AbletonProjects, time);

    public IReadOnlyList<string> Using(IReadOnlyCollection<string> samples) =>
        [.. _sets.Using(samples).Select(Name).Order(StringComparer.OrdinalIgnoreCase)];

    public OpenSet ReadOpenSet() => AbletonSession.Read();

    public SetRelink Relink(IReadOnlyList<SampleMove> moves)
    {
        var result = _sets.Relink(moves, BackupLabel);

        return new SetRelink(
            [.. result.Relinked.Select(Name)],
            [.. result.Failed.Select(failure => Name(failure.Set))]
        );
    }

    private static string Name(string set) => Path.GetFileNameWithoutExtension(set);
}

/// <summary>Which sets now point at the new names, and which could not be changed.</summary>
public sealed record SetRelink(IReadOnlyList<string> Relinked, IReadOnlyList<string> Failed);
