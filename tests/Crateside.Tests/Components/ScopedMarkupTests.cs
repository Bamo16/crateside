namespace Crateside.Tests.Components;

public sealed class ScopedMarkupTests
{
    [Fact]
    public void A_component_with_its_own_stylesheet_builds_no_markup_by_hand_since_that_markup_misses_its_styles()
    {
        var components = Path.Combine(Repository.Root(), "src", "Crateside");

        var unscoped = Directory
            .EnumerateFiles(components, "*.razor.css", SearchOption.AllDirectories)
            .Select(css => css[..^".css".Length])
            .SelectMany(razor =>
                Directory.EnumerateFiles(
                    Path.GetDirectoryName(razor)!,
                    $"{Path.GetFileNameWithoutExtension(razor)}*"
                )
            )
            .Where(file => file.EndsWith(".razor") || file.EndsWith(".cs"))
            .Where(file => File.ReadAllText(file).Contains("builder.OpenElement"))
            .Select(Path.GetFileName);

        Assert.Empty(unscoped);
    }
}
