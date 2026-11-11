using System.IO.Compression;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;

namespace Crateside.Core;

public abstract record BuildFetchOutcome
{
    private BuildFetchOutcome() { }

    public sealed record Ready(string FileName) : BuildFetchOutcome;

    /// <summary>Nothing was installed.</summary>
    public sealed record Failed(string Error) : BuildFetchOutcome;
}

public interface IBuildDownloader
{
    Task DownloadAsync(Uri url, string destination, CancellationToken ct);
}

public sealed class HttpBuildDownloader(IHttpClientFactory clients) : IBuildDownloader
{
    public const string ClientName = "build-downloads";

    public async Task DownloadAsync(Uri url, string destination, CancellationToken ct)
    {
        using var response = await clients
            .CreateClient(ClientName)
            .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);

        response.EnsureSuccessStatusCode();

        await using var source = await response.Content.ReadAsStreamAsync(ct);
        await using var target = File.Create(destination);

        await source.CopyToAsync(target, ct);
    }
}

/// <summary>Puts a pinned build in <c>%LOCALAPPDATA%\crateside\tools</c>, installing nothing the pin's hashes do not vouch for (#163).</summary>
public sealed class BuildFetcher(
    IBuildDownloader downloader,
    CratesidePaths paths,
    ILogger<BuildFetcher> log
)
{
    private readonly Func<FfmpegFilter, BuiltInBuild?> _pins = BuiltInBuilds.For;

    internal BuildFetcher(
        IBuildDownloader downloader,
        CratesidePaths paths,
        ILogger<BuildFetcher> log,
        Func<FfmpegFilter, BuiltInBuild?> pins
    )
        : this(downloader, paths, log) => _pins = pins;

    public BuiltInBuild? PinFor(FfmpegFilter filter) => _pins(filter);

    /// <summary>Whether <paramref name="fileName"/> is a build this fetched.</summary>
    public bool Fetched(string fileName) =>
        Path.GetFullPath(fileName)
            .StartsWith(
                Path.GetFullPath(paths.ToolsFolder) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase
            );

    public async Task<BuildFetchOutcome> EnsureAsync(BuiltInBuild build, CancellationToken ct)
    {
        var target = build.ExecutableIn(paths.ToolsFolder);

        if (File.Exists(target) && await HashAsync(target, ct) == build.ExecutableSha256)
            return new BuildFetchOutcome.Ready(target);

        var folder = Directory.CreateDirectory(Path.GetDirectoryName(target)!).FullName;
        var archive = Path.Combine(folder, $"{build.ArchiveName}.partial");
        var extracted = Path.Combine(folder, $"{Path.GetFileName(target)}.partial");

        try
        {
            log.LogInformation(
                "Fetching {Filter} {Tag} from {Url}",
                FfmpegBuild.NameOf(build.Filter),
                build.Tag,
                build.ArchiveUrl
            );

            await downloader.DownloadAsync(build.ArchiveUrl, archive, ct);

            if (await HashAsync(archive, ct) != build.ArchiveSha256)
                return Failed(build, "the download is not the build that was pinned");

            using (var zip = ZipFile.OpenRead(archive))
            {
                if (zip.GetEntry(build.Entry) is not { } entry)
                    return Failed(build, $"the download has no {build.Entry}");

                await using var from = await entry.OpenAsync(ct);
                await using var to = File.Create(extracted);

                await from.CopyToAsync(to, ct);
            }

            if (await HashAsync(extracted, ct) != build.ExecutableSha256)
                return Failed(build, "the executable in the download is not the one pinned");

            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(
                    extracted,
                    UnixFileMode.UserRead
                        | UnixFileMode.UserWrite
                        | UnixFileMode.UserExecute
                        | UnixFileMode.GroupRead
                        | UnixFileMode.GroupExecute
                        | UnixFileMode.OtherRead
                        | UnixFileMode.OtherExecute
                );

            File.Move(extracted, target, overwrite: true);

            return new BuildFetchOutcome.Ready(target);
        }
        catch (Exception ex)
            when (ex
                    is HttpRequestException
                        or IOException
                        or InvalidDataException
                        or UnauthorizedAccessException
            )
        {
            return Failed(build, ex.Message);
        }
        finally
        {
            File.Delete(archive);
            File.Delete(extracted);
        }
    }

    private BuildFetchOutcome Failed(BuiltInBuild build, string error)
    {
        log.LogError(
            "Could not fetch {Filter} {Tag}: {Error}; nothing was installed",
            FfmpegBuild.NameOf(build.Filter),
            build.Tag,
            error
        );

        return new BuildFetchOutcome.Failed(error);
    }

    private static async Task<string> HashAsync(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);

        return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, ct));
    }
}
