using System.Buffers.Binary;

namespace Pixely.Text;

/// <summary>
/// Reads the pixel size of every bitmap strike in an sfnt font, in the order FreeType exposes them
/// as <c>face->available_sizes</c>. SDL_ttf selects a strike of a bitmap-only face by that index
/// rather than by size and exposes no strike list, so the mapping has to come from the file.
/// </summary>
internal static class SfntBitmapStrikes
{
    private static readonly uint TrueTypeTag = 0x00010000;
    private static readonly uint AppleTrueTypeTag = Tag("true");
    private static readonly uint OpenTypeCffTag = Tag("OTTO");
    private static readonly uint CollectionTag = Tag("ttcf");
    private static readonly uint CblcTag = Tag("CBLC");
    private static readonly uint EblcTag = Tag("EBLC");
    private static readonly uint BlocTag = Tag("bloc");
    private static readonly uint SbixTag = Tag("sbix");

    private const int BitmapSizeTableLength = 48;
    private const int BitmapSizeTablePpemXOffset = 44;
    private const int BitmapSizeTablePpemYOffset = 45;

    /// <summary>
    /// Returns the vertical pixels per em of each usable strike of the first face, or an empty list when the font has no bitmap strikes.
    /// Mirrors FreeType's table precedence (CBLC, EBLC, bloc, then sbix) and its filtering of strikes with a zero ppem.
    /// </summary>
    public static IReadOnlyList<int> ReadPixelSizes(ReadOnlySpan<byte> fontData)
    {
        if (!TryFindTable(fontData, CblcTag, out ReadOnlySpan<byte> table) &&
            !TryFindTable(fontData, EblcTag, out table) &&
            !TryFindTable(fontData, BlocTag, out table))
        {
            return TryFindTable(fontData, SbixTag, out table) ? ReadSbixPixelSizes(table) : Array.Empty<int>();
        }

        return ReadEblcPixelSizes(table);
    }

    private static IReadOnlyList<int> ReadEblcPixelSizes(ReadOnlySpan<byte> table)
    {
        if (table.Length < 8)
        {
            return Array.Empty<int>();
        }

        uint numberOfStrikes = BinaryPrimitives.ReadUInt32BigEndian(table.Slice(4));
        int count = (int)Math.Min(numberOfStrikes, (uint)((table.Length - 8) / BitmapSizeTableLength));
        List<int> pixelSizes = new(count);
        for (int index = 0; index < count; index++)
        {
            ReadOnlySpan<byte> strike = table.Slice(8 + index * BitmapSizeTableLength, BitmapSizeTableLength);
            byte ppemX = strike[BitmapSizeTablePpemXOffset];
            byte ppemY = strike[BitmapSizeTablePpemYOffset];
            if (ppemX != 0 && ppemY != 0)
            {
                pixelSizes.Add(ppemY);
            }
        }

        return pixelSizes;
    }

    private static IReadOnlyList<int> ReadSbixPixelSizes(ReadOnlySpan<byte> table)
    {
        if (table.Length < 8)
        {
            return Array.Empty<int>();
        }

        uint numberOfStrikes = BinaryPrimitives.ReadUInt32BigEndian(table.Slice(4));
        int count = (int)Math.Min(numberOfStrikes, (uint)((table.Length - 8) / 4));
        List<int> pixelSizes = new(count);
        for (int index = 0; index < count; index++)
        {
            uint strikeOffset = BinaryPrimitives.ReadUInt32BigEndian(table.Slice(8 + index * 4));
            if ((ulong)strikeOffset + 4 > (ulong)table.Length)
            {
                continue;
            }

            ushort ppem = BinaryPrimitives.ReadUInt16BigEndian(table.Slice((int)strikeOffset));
            if (ppem != 0)
            {
                pixelSizes.Add(ppem);
            }
        }

        return pixelSizes;
    }

    private static uint Tag(string tag) => BinaryPrimitives.ReadUInt32BigEndian(System.Text.Encoding.ASCII.GetBytes(tag));

    private static bool TryFindTable(ReadOnlySpan<byte> fontData, uint tag, out ReadOnlySpan<byte> table)
    {
        table = default;
        if (fontData.Length < 12)
        {
            return false;
        }

        uint signature = BinaryPrimitives.ReadUInt32BigEndian(fontData);
        // Only an sfnt container has a table directory; parsing a PCF, BDF, FON or WOFF file as one could produce bogus strikes.
        if (signature != TrueTypeTag && signature != AppleTrueTypeTag && signature != OpenTypeCffTag && signature != CollectionTag)
        {
            return false;
        }

        int tableDirectoryOffset = 0;
        // A collection lists its faces' table directories after the header; SDL_ttf opens the first face by default.
        if (signature == CollectionTag)
        {
            uint firstFaceOffset = BinaryPrimitives.ReadUInt32BigEndian(fontData.Slice(12));
            if (firstFaceOffset + 12 > (uint)fontData.Length)
            {
                return false;
            }

            tableDirectoryOffset = (int)firstFaceOffset;
        }

        ushort numberOfTables = BinaryPrimitives.ReadUInt16BigEndian(fontData.Slice(tableDirectoryOffset + 4));
        int recordsOffset = tableDirectoryOffset + 12;
        for (int index = 0; index < numberOfTables; index++)
        {
            int recordOffset = recordsOffset + index * 16;
            if (recordOffset + 16 > fontData.Length)
            {
                return false;
            }

            if (BinaryPrimitives.ReadUInt32BigEndian(fontData.Slice(recordOffset)) != tag)
            {
                continue;
            }

            uint offset = BinaryPrimitives.ReadUInt32BigEndian(fontData.Slice(recordOffset + 8));
            uint length = BinaryPrimitives.ReadUInt32BigEndian(fontData.Slice(recordOffset + 12));
            // FreeType treats an empty table record as absent, so the next table in its precedence is tried.
            if (length == 0 || (ulong)offset + length > (ulong)fontData.Length)
            {
                return false;
            }

            table = fontData.Slice((int)offset, (int)length);
            return true;
        }

        return false;
    }
}
