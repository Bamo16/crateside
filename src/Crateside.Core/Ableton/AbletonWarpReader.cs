using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text;

namespace Crateside.Core.Ableton;

/// <summary>
/// Reads the head and warp section of Ableton's `.asd` sidecar, which is undocumented and described
/// by a type schema the file carries (ADR 0020; the fields are in docs/notes, 2026-09-26).
/// </summary>
public static class AbletonWarpReader
{
    private const byte ClassCode = 0x00;
    private const byte BoolCode = 0x10;
    private const byte Int32Code = 0x11;
    private const byte FloatCode = 0x12;
    private const byte DoubleCode = 0x17;

    private const int HeadCountOffset = 2;
    private const int HeadTableOffset = 6;
    private const int HeadTrailerLength = 20;
    private const int ChunkPreambleLength = 9;

    private const int MarkerRecordLength = 32;
    private const int MarkerIdAllocatorLength = 4;
    private const int ListEndLength = 2;
    private const int MaxTypeCount = 256;

    private const string IsWarpedPath = "IsWarped.Value";
    private const string ModePath = "WarpMode.Value";
    private const string NumeratorPath = "TimeSignature.Numerator.Value";
    private const string DenominatorPath = "TimeSignature.Denominator.Value";
    private const string LoopStartPath = "LoopStart.Value";
    private const string LoopEndPath = "LoopEnd.Value";
    private const string SampleOffsetPath = "SampleOffset.Value";
    private const string OutMarkerPath = "OutMarker.Value";
    private const string LoopOnPath = "LoopOn.Value";

    private static readonly byte[] ChunkMagic = [0xAB, 0x1E, 0x56, 0x78];
    private static readonly byte[] SampleDataTag = [0x00, 0x0A, .. "SampleData"u8];
    private static readonly byte[] WarpMarkerTag = [0x00, 0x0A, .. "WarpMarker"u8];

    public static bool TryRead(string asdPath, [NotNullWhen(true)] out AbletonWarp? warp)
    {
        try
        {
            return TryParse(File.ReadAllBytes(asdPath), out warp);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            warp = null;

            return false;
        }
    }

    public static bool TryParse(ReadOnlySpan<byte> asd, [NotNullWhen(true)] out AbletonWarp? warp)
    {
        warp = null;

        if (
            !TryReadSchema(asd, out var types, out var sampleData, out var instance)
            || !TryReadSavedFlag(asd, sampleData, out var saved)
            || !TryLayout(asd, types, instance, out var leaves, out var markers)
            || !TryBool(asd, leaves, IsWarpedPath, out var isWarped)
            || !TryInt32(asd, leaves, ModePath, out var mode)
            || !TryFloat(asd, leaves, NumeratorPath, out var numerator)
            || !TryFloat(asd, leaves, DenominatorPath, out var denominator)
            || !TryDouble(asd, leaves, LoopStartPath, out var loopStart)
            || !TryDouble(asd, leaves, LoopEndPath, out var loopEnd)
            || !TryDouble(asd, leaves, SampleOffsetPath, out var sampleOffset)
            || !TryDouble(asd, leaves, OutMarkerPath, out var outMarker)
            || !TryBool(asd, leaves, LoopOnPath, out var loopOn)
        )
            return false;

        warp = new AbletonWarp(
            isWarped,
            (WarpMode)mode,
            (int)numerator,
            (int)denominator,
            markers,
            (saved, loopOn) switch
            {
                (false, _) => null,
                // With Loop on, the loop fields hold the loop, not the clip's start and end.
                (true, true) => new DefaultClip(loopStart + sampleOffset, outMarker),
                (true, false) => new DefaultClip(loopStart, loopEnd),
            }
        );

        return true;
    }

    private static bool TryReadSchema(
        ReadOnlySpan<byte> asd,
        [NotNullWhen(true)] out Dictionary<string, SchemaField[]?>? types,
        out int sampleData,
        out int instance
    )
    {
        types = null;
        instance = 0;
        sampleData = asd.IndexOf(SampleDataTag);

        if (sampleData < 0)
            return false;

        var p = sampleData + SampleDataTag.Length;

        if (!TryReadInt32(asd, ref p, out var typeCount) || typeCount is < 1 or > MaxTypeCount)
            return false;

        types = [];

        for (var t = 0; t < typeCount; t++)
        {
            if (
                !TryReadClassName(asd, ref p, out var name)
                || !TryReadInt32(asd, ref p, out var fieldCount)
            )
                return false;

            if (fieldCount < 0)
            {
                types[name] = null;

                continue;
            }

            var fields = new SchemaField[fieldCount];

            for (var f = 0; f < fieldCount; f++)
                if (!TryReadField(asd, ref p, out fields[f]))
                    return false;

            types[name] = fields;
        }

        instance = p;

        return true;
    }

    /// <summary>
    /// The byte ending the head: 1 once <em>Save Default Clip</em> has been pressed. Found by walking
    /// the head's table, and only trusted if that walk lands on the chunk holding the clip.
    /// </summary>
    private static bool TryReadSavedFlag(ReadOnlySpan<byte> asd, int sampleData, out bool saved)
    {
        saved = false;

        var p = HeadCountOffset;

        if (!TryReadInt32(asd, ref p, out var count) || count < 0)
            return false;

        var flag = HeadTableOffset + (long)count * sizeof(int) + HeadTrailerLength;

        if (
            flag + 1 + ChunkPreambleLength != sampleData
            || !asd.Slice((int)flag + 1, ChunkMagic.Length).SequenceEqual(ChunkMagic)
            || asd[(int)flag] is not (0 or 1)
        )
            return false;

        saved = asd[(int)flag] is 1;

        return true;
    }

    private static bool TryReadField(ReadOnlySpan<byte> asd, ref int p, out SchemaField field)
    {
        field = default;

        if (!TryReadUtf16(asd, ref p, out var name) || p >= asd.Length)
            return false;

        var code = asd[p++];

        if (code is not ClassCode)
        {
            field = new SchemaField(name, code, null);

            return true;
        }

        if (p >= asd.Length)
            return false;

        int length = asd[p++];

        if (p + length > asd.Length)
            return false;

        field = new SchemaField(name, code, Encoding.ASCII.GetString(asd.Slice(p, length)));
        p += length;

        return true;
    }

    /// <summary>
    /// Offsets of every fixed-width value in the instance, keyed by dotted path, through the warp
    /// markers and on to the first member whose width the schema cannot give.
    /// </summary>
    private static bool TryLayout(
        ReadOnlySpan<byte> asd,
        Dictionary<string, SchemaField[]?> types,
        int instance,
        [NotNullWhen(true)] out Dictionary<string, int>? leaves,
        [NotNullWhen(true)] out List<WarpMarker>? markers
    )
    {
        leaves = [];
        markers = null;

        if (!types.TryGetValue("SampleData", out var fields) || fields is null)
            return false;

        var offset = instance;

        foreach (var field in fields)
        {
            if (
                field.Class is { } name
                && types.TryGetValue(name, out var nested)
                && nested is null
            )
            {
                if (markers is not null)
                    break;

                offset += MarkerIdAllocatorLength;
                markers = ReadMarkers(asd, offset);
                offset += markers.Count * MarkerRecordLength;

                // A list ends with an empty class name.
                if (
                    offset + ListEndLength > asd.Length
                    || asd.Slice(offset, ListEndLength) is not [0, 0]
                )
                    return false;

                offset += ListEndLength;

                continue;
            }

            if (!TryAppend(types, field, field.Name, ref offset, leaves))
                return markers is not null;
        }

        return markers is not null;
    }

    private static bool TryAppend(
        Dictionary<string, SchemaField[]?> types,
        SchemaField field,
        string path,
        ref int offset,
        Dictionary<string, int> leaves
    )
    {
        if (field.Class is not { } name)
        {
            if (SizeOf(field.Code) is not { } size)
                return false;

            leaves[path] = offset;
            offset += size;

            return true;
        }

        if (!types.TryGetValue(name, out var nested) || nested is null)
            return false;

        foreach (var inner in nested)
            if (!TryAppend(types, inner, $"{path}.{inner.Name}", ref offset, leaves))
                return false;

        return true;
    }

    private static List<WarpMarker> ReadMarkers(ReadOnlySpan<byte> asd, int at)
    {
        List<WarpMarker> markers = [];

        for (var p = at; p + MarkerRecordLength <= asd.Length; p += MarkerRecordLength)
        {
            if (!asd.Slice(p, WarpMarkerTag.Length).SequenceEqual(WarpMarkerTag))
                break;

            markers.Add(
                new WarpMarker(
                    BinaryPrimitives.ReadDoubleLittleEndian(asd[(p + 16)..]),
                    BinaryPrimitives.ReadDoubleLittleEndian(asd[(p + 24)..])
                )
            );
        }

        return markers;
    }

    private static bool TryBool(
        ReadOnlySpan<byte> asd,
        Dictionary<string, int> leaves,
        string path,
        out bool value
    ) => TryRead(asd, leaves, path, bytes => bytes[0] is not 0, out value);

    private static bool TryInt32(
        ReadOnlySpan<byte> asd,
        Dictionary<string, int> leaves,
        string path,
        out int value
    ) => TryRead(asd, leaves, path, BinaryPrimitives.ReadInt32LittleEndian, out value);

    private static bool TryFloat(
        ReadOnlySpan<byte> asd,
        Dictionary<string, int> leaves,
        string path,
        out float value
    ) => TryRead(asd, leaves, path, BinaryPrimitives.ReadSingleLittleEndian, out value);

    private static bool TryDouble(
        ReadOnlySpan<byte> asd,
        Dictionary<string, int> leaves,
        string path,
        out double value
    ) => TryRead(asd, leaves, path, BinaryPrimitives.ReadDoubleLittleEndian, out value);

    private static int? SizeOf(byte code) =>
        code switch
        {
            BoolCode => sizeof(bool),
            Int32Code => sizeof(int),
            FloatCode => sizeof(float),
            DoubleCode => sizeof(double),
            _ => null,
        };

    private static bool TryRead<T>(
        ReadOnlySpan<byte> asd,
        Dictionary<string, int> leaves,
        string path,
        Func<ReadOnlySpan<byte>, T> read,
        out T value
    )
    {
        value = default!;

        if (!TryOffset(leaves, path, Unsafe.SizeOf<T>(), asd.Length, out var at))
            return false;

        value = read(asd[at..]);

        return true;
    }

    private static bool TryOffset(
        Dictionary<string, int> leaves,
        string path,
        int size,
        int length,
        out int offset
    ) => leaves.TryGetValue(path, out offset) && offset >= 0 && offset + size <= length;

    private static bool TryReadInt32(ReadOnlySpan<byte> asd, ref int p, out int value)
    {
        value = 0;

        if (p + sizeof(int) > asd.Length)
            return false;

        value = BinaryPrimitives.ReadInt32LittleEndian(asd[p..]);
        p += sizeof(int);

        return true;
    }

    /// <summary>A big-endian length and the ASCII name that follows it.</summary>
    private static bool TryReadClassName(
        ReadOnlySpan<byte> asd,
        ref int p,
        [NotNullWhen(true)] out string? name
    )
    {
        name = null;

        if (p + sizeof(ushort) > asd.Length)
            return false;

        int length = BinaryPrimitives.ReadUInt16BigEndian(asd[p..]);

        if (p + sizeof(ushort) + length > asd.Length)
            return false;

        name = Encoding.ASCII.GetString(asd.Slice(p + sizeof(ushort), length));
        p += sizeof(ushort) + length;

        return true;
    }

    /// <summary>A little-endian character count and the UTF-16 name that follows it.</summary>
    private static bool TryReadUtf16(
        ReadOnlySpan<byte> asd,
        ref int p,
        [NotNullWhen(true)] out string? name
    )
    {
        name = null;

        if (!TryReadInt32(asd, ref p, out var characters) || characters < 0)
            return false;

        var length = characters * sizeof(char);

        if (p + length > asd.Length)
            return false;

        name = Encoding.Unicode.GetString(asd.Slice(p, length));
        p += length;

        return true;
    }

    private readonly record struct SchemaField(string Name, byte Code, string? Class);
}
