using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Crateside.Core.Staging;

/// <summary>
/// Reads the warp section of Ableton's `.asd` sidecar, which is undocumented and described by a
/// type schema the file carries in its own header.
/// </summary>
/// <remarks>
/// Read-only and never written. Presence of a sidecar says only that Ableton analysed the file
/// (ADR 0002); <see cref="AbletonWarp.Markers"/> is what says how it was warped.
/// </remarks>
public static class AbletonWarpReader
{
    private const byte ClassCode = 0x00;
    private const byte BoolCode = 0x10;
    private const byte Int32Code = 0x11;
    private const byte FloatCode = 0x12;
    private const byte DoubleCode = 0x17;

    private const int MarkerRecordLength = 32;
    private const int MarkerIdAllocatorLength = 4;
    private const int MaxTypeCount = 256;

    private const string IsWarpedPath = "IsWarped.Value";
    private const string ModePath = "WarpMode.Value";
    private const string NumeratorPath = "TimeSignature.Numerator.Value";
    private const string DenominatorPath = "TimeSignature.Denominator.Value";

    private static readonly byte[] SampleDataTag = [0x00, 0x0A, .. "SampleData"u8];
    private static readonly byte[] WarpMarkerTag = [0x00, 0x0A, .. "WarpMarker"u8];

    public static bool TryRead(string asdPath, [NotNullWhen(true)] out AbletonWarp? warp)
    {
        try
        {
            return TryParse(File.ReadAllBytes(asdPath), out warp);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            warp = null;

            return false;
        }
    }

    public static bool TryParse(ReadOnlySpan<byte> asd, [NotNullWhen(true)] out AbletonWarp? warp)
    {
        warp = null;

        if (
            !TryReadSchema(asd, out var types, out var instance)
            || !TryLayout(types, instance, out var leaves, out var markers)
            || !TryOffset(leaves, IsWarpedPath, sizeof(bool), asd.Length, out var warped)
            || !TryOffset(leaves, ModePath, sizeof(int), asd.Length, out var mode)
            || !TryOffset(leaves, NumeratorPath, sizeof(float), asd.Length, out var numerator)
            || !TryOffset(leaves, DenominatorPath, sizeof(float), asd.Length, out var denominator)
        )
            return false;

        warp = new AbletonWarp(
            asd[warped] is not 0,
            (WarpMode)BinaryPrimitives.ReadInt32LittleEndian(asd[mode..]),
            (int)BinaryPrimitives.ReadSingleLittleEndian(asd[numerator..]),
            (int)BinaryPrimitives.ReadSingleLittleEndian(asd[denominator..]),
            ReadMarkers(asd, markers)
        );

        return true;
    }

    private static bool TryReadSchema(
        ReadOnlySpan<byte> asd,
        [NotNullWhen(true)] out Dictionary<string, SchemaField[]?>? types,
        out int instance
    )
    {
        types = null;
        instance = 0;

        var at = asd.IndexOf(SampleDataTag);

        if (at < 0)
            return false;

        var p = at + SampleDataTag.Length;

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
    /// Offsets of every fixed-width value in the instance, keyed by dotted path, up to the first
    /// variable-length member.
    /// </summary>
    /// <remarks>
    /// That member is <c>WarpMarkers</c>, reached through <paramref name="markers"/> instead.
    /// </remarks>
    private static bool TryLayout(
        Dictionary<string, SchemaField[]?> types,
        int instance,
        [NotNullWhen(true)] out Dictionary<string, int>? leaves,
        out int markers
    )
    {
        leaves = [];
        markers = 0;

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
                markers = offset + MarkerIdAllocatorLength;

                return true;
            }

            if (!TryAppend(types, field, field.Name, ref offset, leaves))
                return false;
        }

        return false;
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

    private static int? SizeOf(byte code) =>
        code switch
        {
            BoolCode => sizeof(bool),
            Int32Code => sizeof(int),
            FloatCode => sizeof(float),
            DoubleCode => sizeof(double),
            _ => null,
        };

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
