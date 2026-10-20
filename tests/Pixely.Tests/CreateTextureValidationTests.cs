using Pixely.Content;
using Pixely.Gpu;

namespace Pixely.Tests;

// The checks run before the copy pass touches the device, so a copy pass without one is enough.
public class CreateTextureValidationTests
{
    private static readonly CopyPass _copyPass = new(null!, default, null!);

    [TestCase(TextureFormat.B8G8R8A8Unorm)]
    [TestCase(TextureFormat.R16G16B16A16Float)]
    public void CreateTexture_FormatOtherThanRgba8_Throws(TextureFormat format)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _copyPass.CreateTexture(CreateImage(2, 2), format));
    }

    [Test]
    public void CreateTexture_ImageWithOtherPixelFormat_Throws()
    {
        RawImage image = new(new byte[2 * 2 * 4], new ShortSize(2, 2), PixelFormat.Argb8888);

        Assert.Throws<ArgumentException>(() => _copyPass.CreateTexture(image, TextureFormat.R8G8B8A8UnormSrgb));
    }

    [Test]
    public void CreateTexture_EmptyImage_Throws()
    {
        Assert.Throws<ArgumentException>(() => _copyPass.CreateTexture(new RawImage([], new ShortSize(0, 2), PixelFormat.Abgr8888), TextureFormat.R8G8B8A8Unorm));
    }

    [Test]
    public void CreateTexture_DataLengthDiffersFromSize_Throws()
    {
        RawImage image = new(new byte[2 * 2 * 4 + 4], new ShortSize(2, 2), PixelFormat.Abgr8888);

        Assert.Throws<ArgumentException>(() => _copyPass.CreateTexture(image, TextureFormat.R8G8B8A8Unorm));
    }

    [Test]
    public void CreateTexture_SizeWhoseByteCountOverflowsInt_Throws()
    {
        // 32768 x 32768 x 4 bytes is 2^32, which wraps to 0 in int arithmetic and would match empty data.
        RawImage image = new([], new ShortSize(32768, 32768), PixelFormat.Abgr8888);

        Assert.Throws<ArgumentException>(() => _copyPass.CreateTexture(image, TextureFormat.R8G8B8A8Unorm));
    }

    [TestCase(TextureFormat.B8G8R8A8Unorm)]
    [TestCase(TextureFormat.R16G16B16A16Float)]
    public void CreateTextureArray_FormatOtherThanRgba8_Throws(TextureFormat format)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _copyPass.CreateTextureArray([CreateImage(2, 2)], format));
    }

    [Test]
    public void CreateTextureArray_NoImages_Throws()
    {
        Assert.Throws<ArgumentException>(() => _copyPass.CreateTextureArray([], TextureFormat.R8G8B8A8UnormSrgb));
    }

    [Test]
    public void CreateTextureArray_ImagesOfDifferentSizes_Throws()
    {
        Assert.Throws<ArgumentException>(() => _copyPass.CreateTextureArray([CreateImage(2, 2), CreateImage(2, 4)], TextureFormat.R8G8B8A8UnormSrgb));
    }

    [Test]
    public void CreateTextureArray_LaterLayerWithOtherPixelFormat_Throws()
    {
        RawImage layer = new(new byte[2 * 2 * 4], new ShortSize(2, 2), PixelFormat.Argb8888);

        Assert.Throws<ArgumentException>(() => _copyPass.CreateTextureArray([CreateImage(2, 2), layer], TextureFormat.R8G8B8A8UnormSrgb));
    }

    [Test]
    public void CreateTextureArray_EmptyLayers_Throws()
    {
        RawImage layer = new([], new ShortSize(0, 2), PixelFormat.Abgr8888);

        Assert.Throws<ArgumentException>(() => _copyPass.CreateTextureArray([layer, layer], TextureFormat.R8G8B8A8UnormSrgb));
    }

    [Test]
    public void CreateTextureArray_LaterLayerDataLengthDiffersFromSize_Throws()
    {
        RawImage layer = new(new byte[2 * 2 * 4 + 4], new ShortSize(2, 2), PixelFormat.Abgr8888);

        Assert.Throws<ArgumentException>(() => _copyPass.CreateTextureArray([CreateImage(2, 2), layer], TextureFormat.R8G8B8A8UnormSrgb));
    }

    private static RawImage CreateImage(ushort width, ushort height) => new(new byte[width * height * 4], new ShortSize(width, height), PixelFormat.Abgr8888);
}
