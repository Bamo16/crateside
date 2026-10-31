using System.Text.Json.Nodes;

namespace Crateside.Tests.Api;

/// <summary>The committed <c>docs/openapi.json</c> is the document the code serves; the build rewrites it, CI does not.</summary>
public sealed class OpenApiDocumentTests
{
    [Fact]
    public async Task The_committed_document_matches_the_one_the_API_serves()
    {
        await using var crateside = await SyntheticCrateside.StartAsync();

        var served = JsonNode.Parse(
            await crateside.Client.GetStringAsync(
                "/openapi/v1.json",
                TestContext.Current.CancellationToken
            )
        )!;
        var committed = JsonNode.Parse(
            await File.ReadAllTextAsync(
                Path.Combine(Repository.Root(), "docs", "openapi.json"),
                TestContext.Current.CancellationToken
            )
        )!;

        // The served document names the server it was asked on; the built one has none.
        served.AsObject().Remove("servers");

        Assert.True(
            JsonNode.DeepEquals(committed, served),
            "docs/openapi.json is out of date: build src/Crateside outside CI to regenerate it, and commit the result."
        );
    }

    [Fact]
    public async Task Scalar_is_served_at_its_page()
    {
        await using var crateside = await SyntheticCrateside.StartAsync();

        var page = await crateside.Client.GetStringAsync(
            "/scalar/",
            TestContext.Current.CancellationToken
        );

        Assert.Contains("crateside API", page);
    }
}
