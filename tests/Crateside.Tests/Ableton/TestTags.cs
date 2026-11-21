using AbleKit.Tags;

namespace Crateside.Tests.Ableton;

internal static class TestTags
{
    internal static FileTags Of(
        string relativePath,
        IReadOnlyList<string> keywords,
        IReadOnlyList<string>? hidden = null
    ) =>
        new()
        {
            RelativePath = relativePath,
            Keywords = keywords,
            HiddenKeywords = hidden ?? [],
        };
}
