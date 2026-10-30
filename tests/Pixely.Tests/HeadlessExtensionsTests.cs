using Pixely.App;
using Pixely.Content;
using Pixely.Headless;
using Pixely.Input;
using SDL;

namespace Pixely.Tests;

// Builds real apps, which initializes SDL video with the dummy driver.
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

    [Test]
    public void Build_WithoutTheVariable_RunsAsUsual()
    {
        PixelyAppBuilder builder = new();
        builder.AddHeadlessSupport(static _ => null);

        using IPixelyApp app = builder.Build();

        Assert.Multiple(() =>
        {
            Assert.That(app.GetRequiredService<PixelyFrameClock>(), Is.TypeOf<SdlFrameClock>());
            Assert.That(app.ServiceProvider.GetService<InputAutomation>(), Is.Null);
            Assert.That(app.ServiceProvider.GetService<InputAutomationConsole>(), Is.Null);
            Assert.That(app.GetRequiredService<PixelyConfig>().IsIsolated, Is.False);
        });
    }

    [Test]
    public void Build_WithTheVariable_RunsHeadless()
    {
        PixelyAppBuilder builder = new();
        builder.AddSingleton(new PixelyConfig(EnableCommandConsole: true));
        builder.AddHeadlessSupport(HeadlessOn);

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
    public void Build_WithAnImageWriterRegisteredBefore_KeepsIt()
    {
        RecordingImageWriter imageWriter = new();
        PixelyAppBuilder builder = new();
        builder.AddSingleton<IImageWriter>(imageWriter);
        builder.AddHeadlessSupport(HeadlessOn);

        using IPixelyApp app = builder.Build();

        Assert.That(app.GetRequiredService<IImageWriter>(), Is.SameAs(imageWriter));
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
