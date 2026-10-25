using Crateside.Core.KeyDetection;
using Crateside.Core.Orchestration.Loop;

namespace Crateside.Tests.KeyDetection;

public sealed class KeydetectFilterTests
{
    private const string Build = @"V:\Repos\keydetect-harness\builds\ffmpeg-keydetect-1.3.0.exe";
    private const string Audio = @"V:\Repos\keydetect-harness\audio\adele-hello.flac";

    [Fact]
    public async Task Keydetect_1_3_0_output_is_read_into_a_key_and_a_progression()
    {
        if (!File.Exists(Build) || !File.Exists(Audio))
        {
            Assert.Skip("The keydetect 1.3.0 build or the harness audio is not on this machine");

            return;
        }

        var filter = new KeydetectFilter(new ProcessRunner(), Build);

        var (version, _) = await filter.IdentifyAsync(TestContext.Current.CancellationToken);
        var reading = await filter.ReadAsync(
            Audio,
            window: null,
            TestContext.Current.CancellationToken
        );

        Assert.Contains("keydetect-v1.3.0", version);

        var read = Assert.IsType<KeydetectReading.Read>(reading);

        Assert.InRange(read.R, 0.1, 1);
        Assert.True(read.Chords.Count > 8);
        Assert.NotNull(read.RunnerUp);
        Assert.NotNull(read.Reranked);
    }
}
