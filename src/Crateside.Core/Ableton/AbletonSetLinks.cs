namespace Crateside.Core.Ableton;

/// <summary>The user's sets as a rename sees them: which use a file, which is open, and relinking.</summary>
public interface ISetLinks
{
    IReadOnlyList<string> Using(IReadOnlyCollection<string> samples);

    OpenSet ReadOpenSet();

    SetRelink Relink(IReadOnlyList<SampleMove> moves);
}

public sealed class AbletonSetLinks(CratesidePaths paths, TimeProvider time) : ISetLinks
{
    public IReadOnlyList<string> Using(IReadOnlyCollection<string> samples) =>
        AbletonSets.Using(paths.AbletonProjects, samples);

    public OpenSet ReadOpenSet() => AbletonSession.Read();

    public SetRelink Relink(IReadOnlyList<SampleMove> moves) =>
        AbletonSets.Relink(paths.AbletonProjects, moves, time.GetLocalNow());
}
