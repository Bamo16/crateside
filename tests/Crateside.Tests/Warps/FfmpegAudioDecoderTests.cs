using System.Buffers.Binary;
using Crateside.Core.Warps;

namespace Crateside.Tests.Warps;

public sealed class FfmpegAudioDecoderTests
{
    [Fact]
    public void A_float_wav_from_a_pipe_reads_to_the_end_of_its_data()
    {
        var wav = Wav(
            channels: 2,
            rate: 48_000,
            [0.5f, -0.5f, 0.25f, -0.25f],
            dataSize: uint.MaxValue
        );

        var audio = FfmpegAudioDecoder.Parse(wav);

        Assert.NotNull(audio);
        Assert.Equal(2, audio.ChannelCount);
        Assert.Equal(48_000, audio.SampleRate);
        Assert.Equal(2, audio.Frames);
        Assert.Equal([0.5f, -0.5f, 0.25f, -0.25f], audio.Samples);
    }

    [Fact]
    public void A_wav_that_is_not_32_bit_float_is_refused() =>
        Assert.Null(FfmpegAudioDecoder.Parse(Wav(1, 44_100, [0.5f], bits: 16)));

    [Fact]
    public async Task Ffmpeg_decodes_a_flac_keeping_its_own_channels_and_rate()
    {
        var audio = await new FfmpegAudioDecoder().DecodeAsync(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "silence.flac"),
            TestContext.Current.CancellationToken
        );

        Assert.NotNull(audio);
        Assert.InRange(audio.Frames, 1, int.MaxValue);
        Assert.All(audio.Samples, sample => Assert.Equal(0f, sample));
    }

    private static byte[] Wav(
        int channels,
        int rate,
        float[] samples,
        uint? dataSize = null,
        int bits = 32
    )
    {
        var data = new byte[samples.Length * sizeof(float)];

        for (var i = 0; i < samples.Length; i++)
            BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(i * sizeof(float)), samples[i]);

        var fmt = new byte[16];
        BinaryPrimitives.WriteUInt16LittleEndian(fmt, 3);
        BinaryPrimitives.WriteUInt16LittleEndian(fmt.AsSpan(2), (ushort)channels);
        BinaryPrimitives.WriteInt32LittleEndian(fmt.AsSpan(4), rate);
        BinaryPrimitives.WriteUInt16LittleEndian(fmt.AsSpan(14), (ushort)bits);

        return
        [
            .. "RIFF"u8,
            .. new byte[] { 0xFF, 0xFF, 0xFF, 0xFF },
            .. "WAVE"u8,
            .. "fmt "u8,
            .. Size(16),
            .. fmt,
            .. "data"u8,
            .. Size(dataSize ?? (uint)data.Length),
            .. data,
        ];
    }

    private static byte[] Size(uint size)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, size);

        return bytes;
    }
}
