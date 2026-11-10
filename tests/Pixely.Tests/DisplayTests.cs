using Pixely.App;
using Pixely.DependencyInjection;
using XdlSharp;
using static XdlSharp.Xdl;

namespace Pixely.Tests;

// Builds real apps with SDL's dummy video driver, which has one 1024x768 display at 100 %.
[NonParallelizable]
public class DisplayTests
{
    private static readonly Rectangle DummyBounds = new(0, 0, 1024, 768);

    private IPixelyApp? _app;

    [SetUp]
    public void SetUp()
    {
        SDL_SetHintWithPriority(SDL_HINT_VIDEO_DRIVER, "dummy"u8, SDL_HintPriority.SDL_HINT_DEFAULT);
    }

    [TearDown]
    public void TearDown()
    {
        _app?.Dispose();
    }

    [Test]
    public void GetPrimaryDisplay_WithoutAWindow_ReadsTheDisplay()
    {
        IDisplayService displays = Build(null).GetRequiredService<IDisplayService>();

        Display primary = displays.GetPrimaryDisplay();

        Assert.Multiple(() =>
        {
            Assert.That(primary.Bounds, Is.EqualTo(DummyBounds));
            Assert.That(primary.UsableBounds, Is.EqualTo(DummyBounds));
            Assert.That(primary.PixelDensity, Is.EqualTo(1f));
            Assert.That(primary.ContentScale, Is.EqualTo(1f));
            Assert.That(primary.Scale, Is.EqualTo(1f));
        });
    }

    [Test]
    public void GetDisplays_ListsThePrimaryDisplay()
    {
        IDisplayService displays = Build(null).GetRequiredService<IDisplayService>();

        Assert.That(displays.GetDisplays(), Is.EqualTo(new[] { displays.GetPrimaryDisplay() }));
    }

    [Test]
    public void GetPrimaryDisplay_OffTheMainThread_Throws()
    {
        IDisplayService displays = Build(null).GetRequiredService<IDisplayService>();

        Assert.Throws<PixelyException>(() => Task.Run(displays.GetPrimaryDisplay).GetAwaiter().GetResult());
    }

    [Test]
    public void Centered_OpensTheWindowInTheMiddleOfThePrimaryDisplay()
    {
        Window window = GetWindow(Build(new WindowConfig(Size: (200, 100), Position: WindowPosition.Centered())));

        Assert.That(window.Position, Is.EqualTo(new Vector2Int((1024 - 200) / 2, (768 - 100) / 2)));
    }

    // A display's ID lasts one SDL session, which a test app ends, so the encoding is checked instead of a second app's window.
    [Test]
    public void CenteredOnADisplay_EncodesTheDisplayInSdlCentring()
    {
        Display display = new(new DisplayId((SDL_DisplayID)5), DummyBounds, DummyBounds, 1f, 1f);

        WindowPosition position = WindowPosition.Centered(display);

        Assert.Multiple(() =>
        {
            Assert.That(position.SdlX, Is.EqualTo((int)(SDL_WINDOWPOS_CENTERED_MASK | 5)));
            Assert.That(position.SdlY, Is.EqualTo((int)(SDL_WINDOWPOS_CENTERED_MASK | 5)));
        });
    }

    [Test]
    public void CenteredOnADisplay_IdAboveSixteenBits_Throws()
    {
        Display display = new(new DisplayId((SDL_DisplayID)0x10000), DummyBounds, DummyBounds, 1f, 1f);

        Assert.Throws<ArgumentOutOfRangeException>(() => WindowPosition.Centered(display));
    }

    [Test]
    public void At_OpensTheWindowAtThePoint()
    {
        Window window = GetWindow(Build(new WindowConfig(Size: (200, 100), Position: WindowPosition.At(new Vector2Int(30, 40)))));

        Assert.That(window.Position, Is.EqualTo(new Vector2Int(30, 40)));
    }

    [Test]
    public void Window_OnTheDummyDisplay_ReportsItsDisplayAndUnitScales()
    {
        ServiceProvider provider = Build(new WindowConfig(Size: (200, 100)));
        Window window = GetWindow(provider);

        Assert.Multiple(() =>
        {
            Assert.That(window.Display, Is.EqualTo(provider.GetRequiredService<IDisplayService>().GetPrimaryDisplay()));
            Assert.That(window.PixelDensity, Is.EqualTo(1f));
            Assert.That(window.DisplayScale, Is.EqualTo(1f));
            Assert.That(window.Sync(), Is.True);
        });
    }

    [Test]
    public void WindowDisplay_OffTheMainThread_Throws()
    {
        Window window = GetWindow(Build(new WindowConfig(Size: (200, 100))));

        Assert.Throws<PixelyException>(() => Task.Run(() => window.Display).GetAwaiter().GetResult());
    }

    [Test]
    public void ScaleChanged_SdlScaleEventWithoutAChange_IsNotRaised()
    {
        ServiceProvider provider = Build(new WindowConfig(Size: (200, 100)));
        Window window = GetWindow(provider);
        int raised = 0;
        window.ScaleChanged += _ => raised++;

        SDL_Event evt = default;
        evt.type = (uint)SDL_EventType.SDL_EVENT_WINDOW_DISPLAY_SCALE_CHANGED;
        evt.window.windowID = (SDL_WindowID)window.SdlId;
        unsafe
        {
            Assert.That(SDL_PushEvent(&evt), Is.True, () => SdlError.GetError() ?? string.Empty);
        }

        provider.GetRequiredService<EventService>().Process();

        Assert.That(raised, Is.Zero);
    }

    private ServiceProvider Build(WindowConfig? windowConfig)
    {
        PixelyAppBuilder builder = new();
        if (windowConfig != null)
        {
            builder.AddWindow(windowConfig);
        }

        _app = builder.Build();
        return ((PixelyApp)_app).ServiceProvider;
    }

    private static Window GetWindow(ServiceProvider provider) => provider.GetRequiredService<WindowRegistry>().GetWindow();
}
