using System.Collections.Concurrent;
using System.ComponentModel;
using System.Text;
using Crateside.Core.Orchestration.Loop;

namespace Crateside.Core.Art;

public enum CoverSize
{
    /// <summary>Centre-cropped to a square, 160 px a side, for lists.</summary>
    Small,

    /// <summary>The picture as embedded, unscaled.</summary>
    Full,
}

/// <summary>A cover picture on disk; <see cref="ETag"/> changes whenever the picture would.</summary>
public sealed record CoverImage(string Path, string ContentType, string ETag);

/// <summary>
/// Takes the picture attached to a track's audio file with ffmpeg and keeps it under
/// <see cref="CratesidePaths.ArtFolder"/> until the file it came from changes (#159).
/// </summary>
public sealed class CoverArt(IProcessRunner runner, CratesidePaths paths)
{
    private const int SmallSide = 160;
    private const string FileName = "ffmpeg";
    private const string HasArt = "art:";
    private const string HasNone = "none:";

    private static readonly TimeSpan RunLimit = TimeSpan.FromSeconds(30);

    private readonly SemaphoreSlim _running = new(3);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _tracks = new(
        StringComparer.Ordinal
    );

    /// <summary>
    /// The cover of <paramref name="trackKey"/> from the first of <paramref name="audioPaths"/> that
    /// carries one, extracted on the first call; null when none does or ffmpeg cannot be run.
    /// </summary>
    public async Task<CoverImage?> FindAsync(
        string trackKey,
        IEnumerable<string> audioPaths,
        CoverSize size,
        CancellationToken ct
    )
    {
        var files = audioPaths
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => new FileInfo(path))
            .Where(file => file.Exists)
            .ToList();
        var name = FileNameOf(trackKey);
        var turn = _tracks.GetOrAdd(name, _ => new SemaphoreSlim(1, 1));

        await turn.WaitAsync(ct);

        try
        {
            if (!IsKept(name, files) && !await ExtractAsync(name, files, ct))
                return null;

            return Read(name, size);
        }
        finally
        {
            turn.Release();
        }
    }

    private bool IsKept(string name, List<FileInfo> files) =>
        ReadStamp(name) switch
        {
            null => false,
            var stamp when stamp.StartsWith(HasArt, StringComparison.Ordinal) => files.Count is 0
                || files.Any(file => HasArt + StampOf(file) == stamp),
            var stamp => stamp == NoneStamp(files),
        };

    /// <summary>False when ffmpeg could not be run, which keeps nothing so the next request tries again.</summary>
    private async Task<bool> ExtractAsync(string name, List<FileInfo> files, CancellationToken ct)
    {
        Directory.CreateDirectory(paths.ArtFolder);

        await _running.WaitAsync(ct);

        try
        {
            foreach (var file in files)
            {
                var small = await RunAsync(SmallArguments(file, Temp(name, "small.jpg")), ct);

                if (small is null)
                    return false;

                if (
                    small is true
                    && await RunAsync(FullArguments(file, Temp(name, "full")), ct) is true
                )
                {
                    File.Move(Temp(name, "small.jpg"), Kept(name, "small.jpg"), overwrite: true);
                    File.Move(Temp(name, "full"), Kept(name, "full"), overwrite: true);
                    File.WriteAllText(Kept(name, "stamp"), HasArt + StampOf(file));

                    return true;
                }
            }

            File.Delete(Kept(name, "small.jpg"));
            File.Delete(Kept(name, "full"));
            File.WriteAllText(Kept(name, "stamp"), NoneStamp(files));

            return true;
        }
        finally
        {
            File.Delete(Temp(name, "small.jpg"));
            File.Delete(Temp(name, "full"));
            _running.Release();
        }
    }

    /// <summary>Whether ffmpeg wrote a picture; null when ffmpeg cannot be run.</summary>
    private async Task<bool?> RunAsync(IReadOnlyList<string> arguments, CancellationToken ct)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(RunLimit);

        try
        {
            var outcome = await runner.RunAsync(
                new ProcessRequest(FileName, arguments),
                limit.Token
            );

            return outcome.Succeeded;
        }
        catch (Win32Exception)
        {
            return null;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return false;
        }
    }

    private static string[] SmallArguments(FileInfo file, string output) =>
        [
            .. Input(file),
            "-vf",
            $"crop='min(iw,ih)':'min(iw,ih)',scale={SmallSide}:{SmallSide}",
            "-c:v",
            "mjpeg",
            "-q:v",
            "3",
            .. Output(output),
        ];

    private static string[] FullArguments(FileInfo file, string output) =>
        [.. Input(file), "-c:v", "copy", .. Output(output)];

    private static string[] Input(FileInfo file) =>
        ["-hide_banner", "-loglevel", "error", "-y", "-i", file.FullName, "-an", "-map", "0:v:0"];

    private static string[] Output(string path) =>
        ["-frames:v", "1", "-f", "image2", "-update", "1", path];

    private CoverImage? Read(string name, CoverSize size)
    {
        var path = Kept(name, size is CoverSize.Small ? "small.jpg" : "full");

        return
            File.Exists(path)
            && ReadStamp(name) is { } stamp
            && stamp.StartsWith(HasArt, StringComparison.Ordinal)
            ? new CoverImage(
                path,
                ContentTypeOf(path),
                $"{stamp[HasArt.Length..].Replace(':', '-')}-{size.ToString().ToLowerInvariant()}"
            )
            : null;
    }

    private static string ContentTypeOf(string path)
    {
        Span<byte> head = stackalloc byte[12];

        using var stream = File.OpenRead(path);
        var read = stream.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);

        return head[..read] switch
        {
            [0x89, 0x50, 0x4E, 0x47, ..] => "image/png",
            [
                (byte)'R',
                (byte)'I',
                (byte)'F',
                (byte)'F',
                _,
                _,
                _,
                _,
                (byte)'W',
                (byte)'E',
                (byte)'B',
                (byte)'P',
            ] => "image/webp",
            _ => "image/jpeg",
        };
    }

    private string? ReadStamp(string name) =>
        File.Exists(Kept(name, "stamp")) ? File.ReadAllText(Kept(name, "stamp")) : null;

    private static string StampOf(FileInfo file) => $"{file.Length}:{file.LastWriteTimeUtc.Ticks}";

    private static string NoneStamp(List<FileInfo> files) =>
        HasNone + string.Join(';', files.Select(StampOf));

    private string Kept(string name, string kind) =>
        Path.Combine(paths.ArtFolder, $"{name}.{kind}");

    private string Temp(string name, string kind) =>
        Path.Combine(paths.ArtFolder, $"{name}.{kind}.tmp");

    /// <summary>The key as a file name; a capital is spelled out, because the file system folds case and video IDs do not.</summary>
    private static string FileNameOf(string trackKey) =>
        trackKey
            .Aggregate(
                new StringBuilder(),
                (name, c) =>
                    c switch
                    {
                        (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-' or '_' => name.Append(c),
                        >= 'A' and <= 'Z' => name.Append('^').Append(char.ToLowerInvariant(c)),
                        _ => name.Append('~').Append(((int)c).ToString("x4")),
                    }
            )
            .ToString();
}
