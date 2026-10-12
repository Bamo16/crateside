using Crateside.Core.Beats;

namespace Crateside.Tests.Beats;

public class BeatReaderBuildTests
{
    private const string HarnessBuilds = @"V:\Repos\beatdetect-harness\builds";

    [Fact]
    public async Task A_build_beside_its_build_json_is_identified_by_commit_and_hash()
    {
        if (
            !Directory.Exists(HarnessBuilds)
            || Directory
                .EnumerateDirectories(HarnessBuilds)
                .Select(folder => Path.Combine(folder, "ffmpeg.exe"))
                .FirstOrDefault(File.Exists)
                is not { } executable
        )
        {
            Assert.Skip("No harness build on this machine");

            return;
        }

        var reader = new BeatReader(fileName: executable);

        var build = await reader.RefreshBuildAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(build);
        Assert.NotNull(build.Commit);
        Assert.Equal(64, build.Sha256.Length);
        Assert.EndsWith(build.Commit[..7], build.Label);
    }
}
