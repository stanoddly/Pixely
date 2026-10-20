using Pixely.Gpu;

namespace Pixely.Tests;

public class OffscreenWindowFormatTests
{
    [Test]
    public void SelectColorTargetFormat_BothSupported_PicksBgra()
    {
        TextureFormat format = OffscreenWindow.SelectColorTargetFormat(_ => true);

        Assert.That(format, Is.EqualTo(TextureFormat.B8G8R8A8UnormSrgb));
    }

    [Test]
    public void SelectColorTargetFormat_OnlyRgbaSupported_PicksRgba()
    {
        TextureFormat format = OffscreenWindow.SelectColorTargetFormat(candidate => candidate == TextureFormat.R8G8B8A8UnormSrgb);

        Assert.That(format, Is.EqualTo(TextureFormat.R8G8B8A8UnormSrgb));
    }

    [Test]
    public void SelectColorTargetFormat_NeitherSupported_Throws()
    {
        Assert.Throws<PixelyInitializationException>(() => OffscreenWindow.SelectColorTargetFormat(_ => false));
    }

    // Screenshots read the offscreen target back, so both sRGB formats need a CPU pixel format; the bytes are the same as UNORM's.
    [TestCase(TextureFormat.B8G8R8A8UnormSrgb, TextureFormat.B8G8R8A8Unorm)]
    [TestCase(TextureFormat.R8G8B8A8UnormSrgb, TextureFormat.R8G8B8A8Unorm)]
    public void ToPixelFormat_SrgbFormat_MatchesItsUnormLayout(TextureFormat srgb, TextureFormat unorm)
    {
        Assert.That(srgb.ToPixelFormat(), Is.EqualTo(unorm.ToPixelFormat()));
    }
}
