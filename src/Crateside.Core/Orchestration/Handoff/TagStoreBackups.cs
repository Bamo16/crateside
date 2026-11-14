namespace Crateside.Core.Orchestration.Handoff;

/// <summary>
/// Copies a root's tag store aside before crateside writes it, keeping the newest few (ADR 0052).
/// </summary>
public sealed class TagStoreBackups
{
    private readonly string _root;
    private readonly TimeProvider _time;
    private readonly int _keep;

    public TagStoreBackups(CratesidePaths paths, TimeProvider time)
        : this(paths.TagStoreBackups, time) { }

    internal TagStoreBackups(string root, TimeProvider time, int keep = 20)
    {
        _root = root;
        _time = time;
        _keep = keep;
    }

    public void Save(string tagged)
    {
        var store = Path.Combine(tagged, "Ableton Folder Info");

        if (!Directory.Exists(store))
            return;

        var target = Path.Combine(
            _root,
            $"{_time.GetLocalNow():yyyyMMdd-HHmmss}-{Path.GetFileName(tagged.TrimEnd('\\', '/'))}"
        );

        Directory.CreateDirectory(target);

        foreach (var file in Directory.EnumerateFiles(store, "*.xmp"))
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: true);

        foreach (
            var old in Directory
                .EnumerateDirectories(_root)
                .Order(StringComparer.Ordinal)
                .SkipLast(_keep)
        )
            Directory.Delete(old, recursive: true);
    }
}
