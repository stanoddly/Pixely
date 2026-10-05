using Pixely.Content;
using Pixely.Gpu;

namespace Pixely.Tests;

public class SdlImageWriterTests
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
    public void SavePng_CreatesMissingDirectories()
    {
        string path = Path.Combine(_temporaryDirectory.FullName, "frames", "run", "shot.png");

        new SdlImageWriter().SavePng(CreateImage(), path);

        Assert.That(File.Exists(path), Is.True);
    }

    [Test]
    public void SavePng_WithParentThatIsAFile_Throws()
    {
        string parent = Path.Combine(_temporaryDirectory.FullName, "frames");
        File.WriteAllBytes(parent, []);

        Assert.That(() => new SdlImageWriter().SavePng(CreateImage(), Path.Combine(parent, "shot.png")), Throws.InstanceOf<IOException>());
    }

    private static RawImage CreateImage() => new(new byte[2 * 2 * 4], new ShortSize(2, 2), PixelFormat.Rgba8888);
}
