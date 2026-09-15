using System.Buffers.Binary;
using System.Text;
using Pixely.Text;

namespace Pixely.Tests;

public class SfntBitmapStrikesTests
{
    [Test]
    public void ReadPixelSizes_EblcStrikes_ReturnsPpemYInTableOrder()
    {
        byte[] fontData = BuildFont(("EBLC", BuildEblcTable((11, 11), (14, 14), (20, 16))));

        IReadOnlyList<int> pixelSizes = SfntBitmapStrikes.ReadPixelSizes(fontData);

        Assert.That(pixelSizes, Is.EqualTo(new[] { 11, 14, 16 }));
    }

    [Test]
    public void ReadPixelSizes_BlocStrikes_AreReadLikeEblc()
    {
        byte[] fontData = BuildFont(("bloc", BuildEblcTable((11, 11), (14, 14))));

        IReadOnlyList<int> pixelSizes = SfntBitmapStrikes.ReadPixelSizes(fontData);

        Assert.That(pixelSizes, Is.EqualTo(new[] { 11, 14 }));
    }

    [Test]
    public void ReadPixelSizes_StrikeWithZeroPpem_IsSkippedLikeFreeType()
    {
        byte[] fontData = BuildFont(("EBLC", BuildEblcTable((11, 11), (0, 12), (14, 14))));

        IReadOnlyList<int> pixelSizes = SfntBitmapStrikes.ReadPixelSizes(fontData);

        Assert.That(pixelSizes, Is.EqualTo(new[] { 11, 14 }));
    }

    [Test]
    public void ReadPixelSizes_CblcTakesPrecedenceOverEblc()
    {
        byte[] fontData = BuildFont(("EBLC", BuildEblcTable((11, 11))), ("CBLC", BuildEblcTable((32, 32))));

        IReadOnlyList<int> pixelSizes = SfntBitmapStrikes.ReadPixelSizes(fontData);

        Assert.That(pixelSizes, Is.EqualTo(new[] { 32 }));
    }

    [Test]
    public void ReadPixelSizes_SbixStrikes_ReturnsPpem()
    {
        byte[] fontData = BuildFont(("sbix", BuildSbixTable(20, 32)));

        IReadOnlyList<int> pixelSizes = SfntBitmapStrikes.ReadPixelSizes(fontData);

        Assert.That(pixelSizes, Is.EqualTo(new[] { 20, 32 }));
    }

    [Test]
    public void ReadPixelSizes_NoBitmapTable_ReturnsEmpty()
    {
        byte[] fontData = BuildFont(("glyf", new byte[16]));

        IReadOnlyList<int> pixelSizes = SfntBitmapStrikes.ReadPixelSizes(fontData);

        Assert.That(pixelSizes, Is.Empty);
    }

    [Test]
    public void ReadPixelSizes_Collection_ReadsTheFirstFace()
    {
        byte[] firstFace = BuildFont(("EBLC", BuildEblcTable((11, 11))));
        byte[] collection = new byte[16 + firstFace.Length];
        Encoding.ASCII.GetBytes("ttcf").CopyTo(collection, 0);
        BinaryPrimitives.WriteUInt32BigEndian(collection.AsSpan(4), 0x00010000);
        BinaryPrimitives.WriteUInt32BigEndian(collection.AsSpan(8), 1);
        BinaryPrimitives.WriteUInt32BigEndian(collection.AsSpan(12), 16);
        // The face's table offsets are relative to the file, so shift them past the collection header.
        ushort numberOfTables = BinaryPrimitives.ReadUInt16BigEndian(firstFace.AsSpan(4));
        for (int index = 0; index < numberOfTables; index++)
        {
            Span<byte> offsetField = firstFace.AsSpan(12 + index * 16 + 8);
            BinaryPrimitives.WriteUInt32BigEndian(offsetField, BinaryPrimitives.ReadUInt32BigEndian(offsetField) + 16);
        }
        firstFace.CopyTo(collection, 16);

        IReadOnlyList<int> pixelSizes = SfntBitmapStrikes.ReadPixelSizes(collection);

        Assert.That(pixelSizes, Is.EqualTo(new[] { 11 }));
    }

    [Test]
    public void ReadPixelSizes_TruncatedFile_ReturnsEmpty()
    {
        IReadOnlyList<int> pixelSizes = SfntBitmapStrikes.ReadPixelSizes(new byte[] { 0, 1, 0, 0, 0, 5 });

        Assert.That(pixelSizes, Is.Empty);
    }

    private static byte[] BuildFont(params (string tag, byte[] data)[] tables)
    {
        int directoryLength = 12 + tables.Length * 16;
        int dataLength = tables.Sum(table => (table.data.Length + 3) & ~3);
        byte[] font = new byte[directoryLength + dataLength];
        BinaryPrimitives.WriteUInt32BigEndian(font, 0x00010000);
        BinaryPrimitives.WriteUInt16BigEndian(font.AsSpan(4), (ushort)tables.Length);

        int dataOffset = directoryLength;
        for (int index = 0; index < tables.Length; index++)
        {
            (string tag, byte[] data) = tables[index];
            int recordOffset = 12 + index * 16;
            Encoding.ASCII.GetBytes(tag).CopyTo(font, recordOffset);
            BinaryPrimitives.WriteUInt32BigEndian(font.AsSpan(recordOffset + 8), (uint)dataOffset);
            BinaryPrimitives.WriteUInt32BigEndian(font.AsSpan(recordOffset + 12), (uint)data.Length);
            data.CopyTo(font, dataOffset);
            dataOffset += (data.Length + 3) & ~3;
        }

        return font;
    }

    private static byte[] BuildEblcTable(params (byte ppemX, byte ppemY)[] strikes)
    {
        byte[] table = new byte[8 + strikes.Length * 48];
        BinaryPrimitives.WriteUInt32BigEndian(table, 0x00020000);
        BinaryPrimitives.WriteUInt32BigEndian(table.AsSpan(4), (uint)strikes.Length);
        for (int index = 0; index < strikes.Length; index++)
        {
            table[8 + index * 48 + 44] = strikes[index].ppemX;
            table[8 + index * 48 + 45] = strikes[index].ppemY;
        }

        return table;
    }

    private static byte[] BuildSbixTable(params ushort[] ppems)
    {
        int strikesOffset = 8 + ppems.Length * 4;
        byte[] table = new byte[strikesOffset + ppems.Length * 8];
        BinaryPrimitives.WriteUInt16BigEndian(table, 1);
        BinaryPrimitives.WriteUInt16BigEndian(table.AsSpan(2), 1);
        BinaryPrimitives.WriteUInt32BigEndian(table.AsSpan(4), (uint)ppems.Length);
        for (int index = 0; index < ppems.Length; index++)
        {
            int strikeOffset = strikesOffset + index * 8;
            BinaryPrimitives.WriteUInt32BigEndian(table.AsSpan(8 + index * 4), (uint)strikeOffset);
            BinaryPrimitives.WriteUInt16BigEndian(table.AsSpan(strikeOffset), ppems[index]);
            BinaryPrimitives.WriteUInt16BigEndian(table.AsSpan(strikeOffset + 2), 72);
        }

        return table;
    }
}
