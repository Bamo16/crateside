namespace Crateside.Core.Samples;

/// <summary>Which files on disk are samples at all.</summary>
internal static class SampleFiles
{
    /// <summary>The formats StemForge writes.</summary>
    internal static readonly string[] Extensions = [".flac", ".wav", ".aiff", ".mp3"];

    /// <summary>Whether a path is worth handing to the tag reader — TagLib throws on non-audio.</summary>
    internal static bool IsAudio(string path) =>
        Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
}
