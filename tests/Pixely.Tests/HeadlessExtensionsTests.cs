using Pixely.App;
using Pixely.Content;
using Pixely.Headless;
using Pixely.Input;
using Pixely.Testing;
using SDL;

namespace Pixely.Tests;

// Builds real apps on the stub GPU device, which initializes SDL video with the dummy driver. With headless mode on, the window is an
// offscreen one on the stub device; the app is built but never runs a frame, because its console would read the test's standard input.
[NonParallelizable]
public class HeadlessExtensionsTests
{
    private static readonly Func<string, string?> HeadlessOn = static name => name == HeadlessExtensions.HeadlessVariable ? "1" : null;

    [SetUp]
    public void SetUp()
    {
        // Building initializes SDL video; the dummy driver needs no display, and an environment override still wins.
        SDL3.SDL_SetHintWithPriority(SDL3.SDL_HINT_VIDEO_DRIVER, "dummy", SDL_HintPriority.SDL_HINT_DEFAULT);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("0")]
    [TestCase("false")]
    [TestCase(" FALSE ")]
    public void Build_WithTheVariableOff_RunsAsUsual(string? value)
    {
        PixelyAppBuilder builder = new PixelyAppBuilder().AddStubGpuDevice().AddStubWindows();
        builder.AddHeadlessSupport(name => name == HeadlessExtensions.HeadlessVariable ? value : null);

        using IPixelyApp app = builder.Build();

        Assert.Multiple(() =>
        {
            Assert.That(app.GetRequiredService<PixelyFrameClock>(), Is.TypeOf<SdlFrameClock>());
            Assert.That(app.ServiceProvider.GetService<InputAutomation>(), Is.Null);
            Assert.That(app.ServiceProvider.GetService<InputAutomationConsole>(), Is.Null);
            Assert.That(app.GetRequiredService<PixelyConfig>().IsIsolated, Is.False);
        });
    }

    [TestCase("1")]
    [TestCase("true")]
    [TestCase(" TRUE ")]
    public void Build_WithTheVariableOn_RunsHeadless(string value)
    {
        PixelyAppBuilder builder = new PixelyAppBuilder().AddStubGpuDevice();
        builder.AddSingleton(new PixelyConfig(EnableCommandConsole: true));
        builder.AddHeadlessSupport(name => name == HeadlessExtensions.HeadlessVariable ? value : null);

        using IPixelyApp app = builder.Build();

        PixelyConfig config = app.GetRequiredService<PixelyConfig>();
        Assert.Multiple(() =>
        {
            Assert.That(app.GetRequiredService<PixelyFrameClock>(), Is.TypeOf<FixedStepFrameClock>());
            Assert.That(app.ServiceProvider.GetService<InputAutomation>(), Is.Not.Null);
            Assert.That(app.ServiceProvider.GetService<InputAutomationConsole>(), Is.Not.Null);
            Assert.That(app.GetRequiredService<IImageWriter>(), Is.TypeOf<SdlImageWriter>());
            Assert.That(config.IsIsolated, Is.True);
            // Headless mode reads standard input with its own console.
            Assert.That(config.EnableCommandConsole, Is.False);
            Assert.That(app.ServiceProvider.GetService<AutomationCommandConsole>(), Is.Null);
        });
    }

    [Test]
    public void Build_AfterASecondCall_RegistersHeadlessModeOnce()
    {
        PixelyAppBuilder builder = new PixelyAppBuilder().AddStubGpuDevice();
        builder.AddHeadlessSupport(HeadlessOn);
        builder.AddHeadlessSupport(HeadlessOn);

        using IPixelyApp app = builder.Build();

        Assert.Multiple(() =>
        {
            Assert.That(app.ServiceProvider.GetServices<InputAutomationConsole>(), Has.Count.EqualTo(1));
            Assert.That(app.ServiceProvider.GetServices<IWindowFactory>(), Has.Count.EqualTo(1));
            Assert.That(app.ServiceProvider.GetServices<PixelyFrameClock>(), Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void Build_WithAnImageWriterRegisteredBefore_KeepsIt()
    {
        RecordingImageWriter imageWriter = new();
        PixelyAppBuilder builder = new PixelyAppBuilder().AddStubGpuDevice();
        builder.AddSingleton<IImageWriter>(imageWriter);
        builder.AddHeadlessSupport(HeadlessOn);

        using IPixelyApp app = builder.Build();

        Assert.That(app.GetRequiredService<IImageWriter>(), Is.SameAs(imageWriter));
    }

    // The window's device check runs first, so no frame has to be rendered for it.
    [Test]
    public void CaptureLastFrame_OnTheStubDevice_Throws()
    {
        PixelyAppBuilder builder = new PixelyAppBuilder().AddStubGpuDevice();
        builder.AddHeadlessSupport(HeadlessOn);
        using IPixelyApp app = builder.Build();
        OffscreenWindow window = (OffscreenWindow)app.GetRequiredService<Window>();

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => window.CaptureLastFrame())!;

        Assert.That(exception.Message, Does.StartWith("Headless mode reads frames back").And.Contain(nameof(StubGpuDevice)));
    }

    [Test]
    public void AddHeadlessSupport_WithUnsupportedValue_Throws()
    {
        PixelyAppBuilder builder = new();

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => builder.AddHeadlessSupport(static name => name == HeadlessExtensions.HeadlessVariable ? "yes" : null))!;

        Assert.That(exception.Message, Does.Contain("PIXELY_HEADLESS").And.Contain("yes"));
    }

    private sealed class RecordingImageWriter : IImageWriter
    {
        public void SavePng(Image image, string path)
        {
        }
    }
}
