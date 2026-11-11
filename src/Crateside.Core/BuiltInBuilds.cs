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
            "v1.3.0",
            "ffmpeg-9.0-NoMercy-keydetect-v1.3.0.zip",
            "5f16f73e2e5b127fc3db0389d5c73265086f512168655660c21d1e7100670833",
            "ffmpeg-windows/ffmpeg.exe",
            "28f445ee6e65c4337e8fca617763af9e3816af7f3920983edcf34b16a7173a96"
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
            "v1.3.0",
            "ffmpeg-9.0-NoMercy-keydetect-v1.3.0.zip",
            "5f16f73e2e5b127fc3db0389d5c73265086f512168655660c21d1e7100670833",
            "ffmpeg-linux/ffmpeg",
            "35a52f68b043f796c789baf54b21005b387d14f2c9687407d95235fa3d4b0ed8"
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
