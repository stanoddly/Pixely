using Pixely.Headless;
using Pixely.Testing;

namespace Pixely.Tests;

public class OffscreenWindowFactoryTests
{
    // The device is refused before any SDL window exists, so this needs no video driver.
    [Test]
    public void CreateWindow_OnTheStubDevice_Throws()
    {
        using PixelyFactory pixelyFactory = new(new PixelyConfig(), null);
        OffscreenWindowFactory factory = new(pixelyFactory);

        PixelyInitializationException exception = Assert.Throws<PixelyInitializationException>(
            () => factory.CreateWindow(new ViewScope(0), StubGpuDevice.Create(), new FixedStepFrameClock(), new WindowConfig(), new PlatformInfo(null)))!;

        Assert.That(exception.Message, Does.StartWith("Headless mode reads frames back").And.Contain(nameof(StubGpuDevice)));
    }
}
