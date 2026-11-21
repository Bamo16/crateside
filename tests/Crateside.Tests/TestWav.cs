namespace Crateside.Tests;

/// <summary>Writes a mono 16-bit 44.1 kHz WAV whose samples, from -1 to 1, are a function of time in seconds.</summary>
internal static class TestWav
{
    private const int Rate = 44100;

    public static void Write(string path, double seconds, Func<double, double> sampleAt)
    {
        var samples = (int)(seconds * Rate);
        using var writer = new BinaryWriter(File.Create(path));

        writer.Write("RIFF"u8);
        writer.Write(36 + samples * 2);
        writer.Write("WAVEfmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(Rate);
        writer.Write(Rate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(samples * 2);

        for (var i = 0; i < samples; i++)
            writer.Write((short)(Math.Clamp(sampleAt((double)i / Rate), -1, 1) * short.MaxValue));
    }
}
