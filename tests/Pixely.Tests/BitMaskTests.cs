using System.Collections;

namespace Pixely.Tests;

public sealed class BitMaskTests
{
    [Test]
    public void Constructor_DefaultValue_ClearsAllBits()
    {
        BitMask mask = new((3, 2));

        Assert.Multiple(() =>
        {
            Assert.That(mask[0, 0], Is.False);
            Assert.That(mask[2, 1], Is.False);
        });
    }

    [Test]
    public void Constructor_InitialValue_SetsAllBits()
    {
        BitMask mask = new((3, 2), true);

        Assert.Multiple(() =>
        {
            Assert.That(mask[0, 0], Is.True);
            Assert.That(mask[2, 1], Is.True);
        });
    }

    [Test]
    public void Indexer_SetsCoordinatesIndependently()
    {
        BitMask mask = new((3, 2));

        mask[1, 1] = true;

        Assert.Multiple(() =>
        {
            Assert.That(mask[1, 1], Is.True);
            Assert.That(mask[0, 1], Is.False);
            Assert.That(mask[2, 1], Is.False);
        });
    }

    [Test]
    public void BitArrayConstructor_UsesRowMajorOrderAndCopiesBits()
    {
        BitArray bits = new(6);
        bits[4] = true;
        BitMask mask = new((3, 2), bits);

        bits[4] = false;

        Assert.That(mask[1, 1], Is.True);
    }

    [Test]
    public void BitArrayConstructor_WithWrongBitCount_Throws()
    {
        BitArray bits = new(5);

        Assert.That(() => new BitMask((3, 2), bits), Throws.ArgumentException.With.Property("ParamName").EqualTo("bits"));
    }

    [TestCase(-1, 0, "x")]
    [TestCase(3, 0, "x")]
    [TestCase(0, -1, "y")]
    [TestCase(0, 2, "y")]
    public void Indexer_WithCoordinatesOutsideMask_Throws(int x, int y, string parameterName)
    {
        BitMask mask = new((3, 2));

        Assert.That(() => _ = mask[x, y], Throws.TypeOf<ArgumentOutOfRangeException>().With.Property("ParamName").EqualTo(parameterName));
    }
}
