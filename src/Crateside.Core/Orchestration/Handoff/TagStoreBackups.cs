namespace Crateside.Core.Orchestration.Handoff;

/// <summary>
/// Copies a root's tag store aside before crateside writes it, keeping the newest few (ADR 0052).
/// </summary>
public sealed class TagStoreBackups(string root, TimeProvider time, int keep = 20)
{
    public static string DefaultRoot { get; } =
        Environment.SpecialFolder.ApplicationData.GetFolderPath(
            "crateside",
            "backups",
            "tag-stores"
        );

    public void Save(string tagged)
    {
        var store = Path.Combine(tagged, "Ableton Folder Info");

        if (!Directory.Exists(store))
            return;

        var target = Path.Combine(
            root,
            $"{time.GetLocalNow():yyyyMMdd-HHmmss}-{Path.GetFileName(tagged.TrimEnd('\\', '/'))}"
        );

        Directory.CreateDirectory(target);

        foreach (var file in Directory.EnumerateFiles(store, "*.xmp"))
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: true);

        foreach (
            var old in Directory
                .EnumerateDirectories(root)
                .Order(StringComparer.Ordinal)
                .SkipLast(keep)
        )
            Directory.Delete(old, recursive: true);
    }
}
