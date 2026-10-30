using Pixely.Content;
using Pixely.Gpu;
using Pixely.Headless;

namespace Pixely.Tests;

public class SdlImageDecoderTests
{
    private DirectoryInfo _temporaryDirectory = null!;

    [SetUp]
    public void SetUp()
    {
        _temporaryDirectory = Directory.CreateTempSubdirectory("Pixely.Tests-");
    }

    [TearDown]
    public void TearDown()
    {
        _temporaryDirectory.Delete(recursive: true);
    }

    [Test]
    public void Decode_Png_GivesTightlyPackedRgba()
    {
        byte[] pixels = [255, 0, 0, 255, 0, 255, 0, 255, 0, 0, 255, 255, 255, 255, 255, 128, 1, 2, 3, 4, 5, 6, 7, 8];
        string path = Path.Combine(_temporaryDirectory.FullName, "image.png");
        new SdlImageWriter().SavePng(new RawImage(pixels, new ShortSize(3, 2), PixelFormat.Abgr8888), path);

        using Image image = new SdlImageDecoder().Decode(File.ReadAllBytes(path));

        Assert.That(image.Size, Is.EqualTo(new ShortSize(3, 2)));
        Assert.That(image.PixelFormat, Is.EqualTo(PixelFormat.Abgr8888));
        Assert.That(image.Data.ToArray(), Is.EqualTo(pixels));
    }

    [Test]
    public void Decode_CorruptBytes_Throws()
    {
        Assert.Throws<InvalidDataException>(() => new SdlImageDecoder().Decode([0x89, 0x50, 0x4E, 0x47, 1, 2, 3, 4]));
    }
}
