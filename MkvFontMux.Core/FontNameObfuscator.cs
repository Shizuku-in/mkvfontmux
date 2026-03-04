using System.Buffers.Binary;
using System.Text;

namespace MkvFontMux;

internal static class FontNameObfuscator
{
    private static readonly HashSet<ushort> TargetNameIds = [1, 4, 6];
    private static readonly byte[] TtcTag = Encoding.ASCII.GetBytes("ttcf");

    public static bool TryObfuscate(string fontPath, string newFamilyName, out string? error)
        => TryObfuscate(fontPath, newFamilyName, 0, out error);

    public static bool TryObfuscate(string fontPath, string newFamilyName, int fontIndex, out string? error)
    {
        error = null;

        try
        {
            var data = File.ReadAllBytes(fontPath);
            if (!TryGetSfntBounds(data, fontIndex, out var sfntOffset, out var sfntSpanLength, out error))
            {
                return false;
            }

            if (!TryGetTableInfo(data, sfntOffset, "name", out var nameOffset, out var nameLength, out _) ||
                !TryGetTableInfo(data, sfntOffset, "head", out var headOffset, out _, out _))
            {
                error = "Font is missing required 'name' or 'head' table.";
                return false;
            }

            if (!PatchNameTableInPlace(data, nameOffset, nameLength, newFamilyName))
            {
                error = "No writable name records were found for obfuscation.";
                return false;
            }

            RecalculateChecksums(data, sfntOffset, sfntSpanLength, headOffset);
            File.WriteAllBytes(fontPath, data);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static bool TryGetSfntBounds(byte[] data, int fontIndex, out int sfntOffset, out int sfntSpanLength, out string? error)
    {
        sfntOffset = 0;
        sfntSpanLength = 0;
        error = null;

        if (data.Length < 12)
        {
            error = "Font file is too small.";
            return false;
        }

        var isTtc = data.AsSpan(0, 4).SequenceEqual(TtcTag);
        if (!isTtc)
        {
            if (fontIndex != 0)
            {
                error = "fontIndex > 0 is only valid for TTC files.";
                return false;
            }

            sfntOffset = 0;
            if (!TryComputeSfntSpanLength(data, sfntOffset, out sfntSpanLength))
            {
                error = "Invalid sfnt structure.";
                return false;
            }

            return true;
        }

        if (data.Length < 16)
        {
            error = "Invalid TTC header.";
            return false;
        }

        var numFonts = checked((int)ReadU32(data, 8));
        if (fontIndex < 0 || fontIndex >= numFonts)
        {
            error = $"TTC fontIndex out of range: {fontIndex}, count={numFonts}.";
            return false;
        }

        var offsetPos = 12 + (fontIndex * 4);
        if (offsetPos + 4 > data.Length)
        {
            error = "TTC font offset table is truncated.";
            return false;
        }

        sfntOffset = checked((int)ReadU32(data, offsetPos));
        if (sfntOffset < 0 || sfntOffset + 12 > data.Length)
        {
            error = "TTC sfnt offset is invalid.";
            return false;
        }

        if (!TryComputeSfntSpanLength(data, sfntOffset, out sfntSpanLength))
        {
            error = "Unable to compute TTC sfnt span length.";
            return false;
        }

        return true;
    }

    private static bool TryComputeSfntSpanLength(byte[] data, int sfntOffset, out int spanLength)
    {
        spanLength = 0;
        if (sfntOffset < 0 || sfntOffset + 12 > data.Length)
        {
            return false;
        }

        var numTables = ReadU16(data, sfntOffset + 4);
        var tableDirOffset = sfntOffset + 12;
        var maxEnd = tableDirOffset + (numTables * 16);

        for (var i = 0; i < numTables; i++)
        {
            var record = tableDirOffset + i * 16;
            if (record + 16 > data.Length)
            {
                return false;
            }

            var tableOffset = checked((int)ReadU32(data, record + 8));
            var tableLength = checked((int)ReadU32(data, record + 12));
            if (tableOffset < 0 || tableLength < 0 || tableOffset + tableLength > data.Length)
            {
                return false;
            }

            maxEnd = Math.Max(maxEnd, tableOffset + tableLength);
        }

        spanLength = Math.Max(0, maxEnd - sfntOffset);
        return spanLength > 0;
    }

    private static bool TryGetTableInfo(byte[] data, int sfntOffset, string tag, out int tableOffset, out int tableLength, out int recordOffset)
    {
        tableOffset = 0;
        tableLength = 0;
        recordOffset = 0;

        if (sfntOffset < 0 || sfntOffset + 12 > data.Length)
        {
            return false;
        }

        var numTables = ReadU16(data, sfntOffset + 4);
        var tableDirOffset = sfntOffset + 12;
        for (var i = 0; i < numTables; i++)
        {
            var offset = tableDirOffset + i * 16;
            if (offset + 16 > data.Length)
            {
                return false;
            }

            var currentTag = Encoding.ASCII.GetString(data, offset, 4);
            if (!string.Equals(currentTag, tag, StringComparison.Ordinal))
            {
                continue;
            }

            tableOffset = checked((int)ReadU32(data, offset + 8));
            tableLength = checked((int)ReadU32(data, offset + 12));
            recordOffset = offset;
            return tableOffset >= 0 && tableLength > 0 && tableOffset + tableLength <= data.Length;
        }

        return false;
    }

    private static bool PatchNameTableInPlace(byte[] data, int nameOffset, int nameLength, string familyName)
    {
        if (nameOffset + 6 > data.Length)
        {
            return false;
        }

        var count = ReadU16(data, nameOffset + 2);
        var stringOffset = ReadU16(data, nameOffset + 4);
        var recordsOffset = nameOffset + 6;
        var storageStart = nameOffset + stringOffset;
        var tableEnd = nameOffset + nameLength;
        var modified = false;

        for (var i = 0; i < count; i++)
        {
            var rec = recordsOffset + i * 12;
            if (rec + 12 > data.Length)
            {
                break;
            }

            var platformId = ReadU16(data, rec);
            var encodingId = ReadU16(data, rec + 2);
            var nameId = ReadU16(data, rec + 6);
            if (!TargetNameIds.Contains(nameId))
            {
                continue;
            }

            var existingLength = ReadU16(data, rec + 8);
            var offsetInStorage = ReadU16(data, rec + 10);
            var storagePos = storageStart + offsetInStorage;
            if (storagePos < 0 || storagePos + existingLength > tableEnd || existingLength == 0)
            {
                continue;
            }

            var sourceName = nameId == 6 ? ToPostScriptName(familyName) : familyName;
            var encoded = EncodeName(sourceName, platformId, encodingId);
            if (encoded.Length > existingLength)
            {
                encoded = TruncateForSlot(encoded, existingLength, platformId);
            }

            data.AsSpan(storagePos, existingLength).Clear();
            encoded.CopyTo(data, storagePos);
            modified = true;
        }

        return modified;
    }

    private static byte[] TruncateForSlot(byte[] encoded, int maxLength, ushort platformId)
    {
        if (maxLength <= 0)
        {
            return [];
        }

        var length = Math.Min(encoded.Length, maxLength);
        if ((platformId == 0 || platformId == 3) && (length % 2 != 0))
        {
            length -= 1;
        }

        if (length <= 0)
        {
            return [];
        }

        return encoded.AsSpan(0, length).ToArray();
    }

    private static byte[] EncodeName(string name, ushort platformId, ushort encodingId)
    {
        if (platformId == 0 || platformId == 3)
        {
            return Encoding.BigEndianUnicode.GetBytes(name);
        }

        if (platformId == 1)
        {
            return Encoding.ASCII.GetBytes(name);
        }

        return encodingId == 1
            ? Encoding.BigEndianUnicode.GetBytes(name)
            : Encoding.ASCII.GetBytes(name);
    }

    private static string ToPostScriptName(string input)
    {
        var chars = input.Where(static ch => char.IsLetterOrDigit(ch) || ch is '-' or '_').ToArray();
        return chars.Length > 0 ? new string(chars) : "FontAlias";
    }

    private static void RecalculateChecksums(byte[] data, int sfntOffset, int sfntSpanLength, int headOffset)
    {
        WriteU32(data, headOffset + 8, 0);

        var numTables = ReadU16(data, sfntOffset + 4);
        var tableDirOffset = sfntOffset + 12;
        for (var i = 0; i < numTables; i++)
        {
            var recordOffset = tableDirOffset + i * 16;
            if (recordOffset + 16 > data.Length)
            {
                break;
            }

            var tableOffset = checked((int)ReadU32(data, recordOffset + 8));
            var tableLength = checked((int)ReadU32(data, recordOffset + 12));
            if (tableOffset < 0 || tableLength <= 0 || tableOffset + tableLength > data.Length)
            {
                continue;
            }

            var checksum = ComputeChecksum(data, tableOffset, tableLength);
            WriteU32(data, recordOffset + 4, checksum);
        }

        var sfntChecksum = ComputeChecksum(data, sfntOffset, sfntSpanLength);
        var checksumAdjustment = unchecked(0xB1B0AFBAu - sfntChecksum);
        WriteU32(data, headOffset + 8, checksumAdjustment);
    }

    private static uint ComputeChecksum(byte[] data, int offset, int length)
    {
        uint sum = 0;
        var end = offset + length;
        var paddedEnd = (end + 3) & ~3;
        Span<byte> scratch = stackalloc byte[4];

        for (var pos = offset; pos < paddedEnd; pos += 4)
        {
            uint value = 0;
            if (pos + 4 <= data.Length)
            {
                value = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(pos, 4));
            }
            else
            {
                scratch.Clear();
                var available = Math.Max(0, data.Length - pos);
                if (available > 0)
                {
                    data.AsSpan(pos, available).CopyTo(scratch);
                }

                value = BinaryPrimitives.ReadUInt32BigEndian(scratch);
            }

            sum = unchecked(sum + value);
        }

        return sum;
    }

    private static ushort ReadU16(byte[] data, int offset) => BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset, 2));
    private static uint ReadU32(byte[] data, int offset) => BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset, 4));

    private static void WriteU32(byte[] data, int offset, uint value) => BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(offset, 4), value);
}
