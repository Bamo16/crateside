namespace Crateside.Core;

/// <summary>
/// Lists folders on this machine for a page to choose from; a browser cannot hand a server a
/// real path, and the page may not be on this machine.
/// </summary>
public sealed class FolderBrowser
{
    /// <summary>
    /// The folders directly under <paramref name="path"/>, or the drives when it is empty or
    /// gone. A path that no longer exists lists its nearest existing ancestor.
    /// </summary>
    public FolderListing List(string? path)
    {
        var folder = NearestExisting(path);

        return folder is null
            ? new FolderListing(string.Empty, null, Drives())
            : new FolderListing(
                folder.FullName,
                folder.Parent?.FullName ?? string.Empty,
                Children(folder)
            );
    }

    private static DirectoryInfo? NearestExisting(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        try
        {
            for (var folder = new DirectoryInfo(path); folder is not null; folder = folder.Parent)
            {
                if (folder.Exists)
                    return folder;
            }
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException)
        {
            // Not a path at all.
        }

        return null;
    }

    private static List<FolderEntry> Drives() =>
        [
            .. DriveInfo
                .GetDrives()
                .Where(drive => drive.IsReady)
                .Select(drive => new FolderEntry(drive.Name, drive.Name)),
        ];

    private static List<FolderEntry> Children(DirectoryInfo folder)
    {
        try
        {
            return
            [
                .. folder
                    .EnumerateDirectories()
                    .Where(child =>
                        (child.Attributes & (FileAttributes.Hidden | FileAttributes.System)) is 0
                    )
                    .OrderBy(child => child.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(child => new FolderEntry(child.Name, child.FullName)),
            ];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}

/// <summary>A folder's place in the listing. <paramref name="Path"/> is empty for the drives.</summary>
public sealed record FolderListing(string Path, string? Parent, IReadOnlyList<FolderEntry> Folders);

public sealed record FolderEntry(string Name, string Path);
