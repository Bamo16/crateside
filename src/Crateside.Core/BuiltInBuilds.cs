namespace Crateside.Core;

/// <summary>The ffmpeg build crateside fetches for a filter, pinned by release and hash (#163).</summary>
/// <param name="Tag">The release tag in FiLL's repository for the filter.</param>
/// <param name="ArchiveName">The release's zip, whose hash is in the release's <c>SHA256SUMS.txt</c>.</param>
/// <param name="Entry">The executable's path inside the zip.</param>
public sealed record BuiltInBuild(
    FfmpegFilter Filter,
    string Tag,
    string ArchiveName,
    string ArchiveSha256,
    string Entry,
    string ExecutableSha256
)
{
    public Uri ArchiveUrl =>
        new(
            $"{BuiltInBuilds.Releases}/ffmpeg-{FfmpegBuild.NameOf(Filter)}/releases/download/{Tag}/{ArchiveName}"
        );

    /// <summary>Where the executable is kept, in a folder of its own so a new pin never replaces an old build.</summary>
    public string ExecutableIn(string toolsFolder) =>
        Path.Combine(toolsFolder, $"{FfmpegBuild.NameOf(Filter)}-{Tag}", Path.GetFileName(Entry));
}

/// <summary>
/// One build per filter, in code, which a crateside update moves (ADR 0093). The versions are the
/// ones the maintainer's pins in <c>builds.json</c> read on, with the hashes of the release's own
/// <c>SHA256SUMS.txt</c>.
/// </summary>
public static class BuiltInBuilds
{
    internal const string Releases = "https://forgejo.phillippepelzer.me/FiLL";

    private static readonly BuiltInBuild[] Windows =
    [
        new(
            FfmpegFilter.Keydetect,
            "v1.6.0",
            "ffmpeg-9.0-NoMercy-keydetect-v1.6.0.zip",
            "87fe09e4e6d7af5a1a7e1097918902848041be8581ad181c391d499c3b36c25a",
            "ffmpeg-windows/ffmpeg.exe",
            "5a918a37d7dd2c3991d3c8203cb12fff9117bafa0a70ad41db753952a47fe7b2"
        ),
        new(
            FfmpegFilter.Beatdetect,
            "v1.4.0",
            "ffmpeg-9.0-NoMercy-beatdetect-v1.4.0.zip",
            "dc298e6496b4a205cf86bce78e9bfe8d1813c8ae80c880c70961efc121221cd8",
            "ffmpeg-windows/ffmpeg.exe",
            "85d363d5f204822a435f588f63c4fad7bd0ca67a1343edac11ef233182ad8120"
        ),
    ];

    private static readonly BuiltInBuild[] Linux =
    [
        new(
            FfmpegFilter.Keydetect,
            "v1.6.0",
            "ffmpeg-9.0-NoMercy-keydetect-v1.6.0.zip",
            "87fe09e4e6d7af5a1a7e1097918902848041be8581ad181c391d499c3b36c25a",
            "ffmpeg-linux/ffmpeg",
            "3aefab570a6f3d07e971cc75039e552913d88d03d1dee00affdec66ef45ddde5"
        ),
        new(
            FfmpegFilter.Beatdetect,
            "v1.4.0",
            "ffmpeg-9.0-NoMercy-beatdetect-v1.4.0.zip",
            "dc298e6496b4a205cf86bce78e9bfe8d1813c8ae80c880c70961efc121221cd8",
            "ffmpeg-linux/ffmpeg",
            "7f752c3fc5e13204d871ccc4e4aa806ac72d0704127d9f0a5cc3f52b6ea1a81d"
        ),
    ];

    /// <summary>Null where no build is published for this system.</summary>
    public static BuiltInBuild? For(FfmpegFilter filter) =>
        (
            OperatingSystem.IsWindows() ? Windows
            : OperatingSystem.IsLinux() ? Linux
            : []
        ).FirstOrDefault(build => build.Filter == filter);
}
