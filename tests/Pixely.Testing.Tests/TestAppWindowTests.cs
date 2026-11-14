using Pixely.App;

namespace Pixely.Testing.Tests;

// How a window of a test app behaves, which a game's tests rely on.
public class TestAppWindowTests
{
    [Test]
    public void Create_WithoutASize_IsCenteredOnTheDisplayAtTheDefaultSize()
    {
        using IPixelyApp app = BuildWithWindow(new WindowConfig());
        Window window = app.GetRequiredService<Window>();

        Assert.Multiple(() =>
        {
            Assert.That(window.Size, Is.EqualTo(new Size<uint>(640, 480)));
            Assert.That(window.RenderSizeInPixels, Is.EqualTo(new ShortSize(640, 480)));
            Assert.That(window.PixelDensity, Is.EqualTo(1f));
            Assert.That(window.DisplayScale, Is.EqualTo(1f));
            Assert.That(window.Position, Is.EqualTo(new Vector2Int(192, 144)));
            Assert.That(window.IsVisible, Is.True);
            Assert.That(window.IsRenderable, Is.True);
        });
    }

    [Test]
    public void Create_WithASizeAndPosition_UsesThem()
    {
        using IPixelyApp app = BuildWithWindow(new WindowConfig(Size: (200, 100), Position: WindowPosition.At(new Vector2Int(10, 20))));
        Window window = app.GetRequiredService<Window>();

        Assert.That((window.Size, window.Position), Is.EqualTo((new Size<uint>(200, 100), new Vector2Int(10, 20))));
    }

    [Test]
    public void Create_Fullscreen_CoversTheDisplay()
    {
        using IPixelyApp app = BuildWithWindow(new WindowConfig(Fullscreen: true));
        Window window = app.GetRequiredService<Window>();

        Assert.That((window.Size, window.Position), Is.EqualTo((new Size<uint>(1024, 768), new Vector2Int(0, 0))));
    }

    [Test]
    public void FullscreenBorderless_LeftByAWindowCreatedFullscreen_RestoresItsConfiguredSizeCentred()
    {
        using IPixelyApp app = BuildWithWindow(new WindowConfig(Size: (300, 200), Fullscreen: true));
        Window window = app.GetRequiredService<Window>();
        List<ResolutionChangedEventArgs> changes = [];
        window.ResolutionChanged += changes.Add;

        window.SetFullscreenBorderless(false);
        app.RunFrame();

        Assert.Multiple(() =>
        {
            Assert.That((window.Size, window.Position), Is.EqualTo((new Size<uint>(300, 200), new Vector2Int(362, 284))));
            Assert.That(changes.Select(change => change.NewSize), Is.EqualTo(new[] { new ShortSize(300, 200) }));
        });
    }

    [Test]
    public void FileDialogs_WithAFilterWithoutAPattern_Throw()
    {
        using IPixelyApp app = BuildWithWindow(new WindowConfig());
        Window window = app.GetRequiredService<Window>();
        FileDialogFilter[] filters = [new FileDialogFilter("Images", null!)];

        Assert.Multiple(() =>
        {
            Assert.That(() => window.ShowModalOpenFileDialog(filters), Throws.ArgumentException);
            Assert.That(() => window.ShowModalSaveFileDialog(filters), Throws.ArgumentException);
        });
    }

    [Test]
    public void Create_Hidden_IsNeitherVisibleNorRenderable()
    {
        using IPixelyApp app = BuildWithWindow(new WindowConfig(InitiallyVisible: false));
        Window window = app.GetRequiredService<Window>();

        Assert.That((window.IsVisible, window.IsRenderable), Is.EqualTo((false, false)));
    }

    [Test]
    public void ShowAndHide_ChangeVisibility()
    {
        using IPixelyApp app = BuildWithWindow(new WindowConfig());
        Window window = app.GetRequiredService<Window>();

        bool hidden = window.Hide();
        (bool, bool) afterHide = (window.IsVisible, window.IsRenderable);
        bool shown = window.Show();

        Assert.Multiple(() =>
        {
            Assert.That(hidden, Is.True);
            Assert.That(afterHide, Is.EqualTo((false, false)));
            Assert.That(shown, Is.True);
            Assert.That((window.IsVisible, window.IsRenderable), Is.EqualTo((true, true)));
        });
    }

    [Test]
    public void Minimize_ReturnsFalseAndLeavesTheWindowRenderable()
    {
        using IPixelyApp app = BuildWithWindow(new WindowConfig());
        Window window = app.GetRequiredService<Window>();

        Assert.That((window.Minimize(), window.IsRenderable), Is.EqualTo((false, true)));
    }

    [Test]
    public void RaiseAndSync_ReturnTrue()
    {
        using IPixelyApp app = BuildWithWindow(new WindowConfig());
        Window window = app.GetRequiredService<Window>();

        Assert.That((window.Raise(), window.Sync()), Is.EqualTo((true, true)));
    }

    [Test]
    public void SetSize_ReadsBackAtOnce_AndRaisesResolutionChangedInTheNextFrame()
    {
        using IPixelyApp app = BuildWithWindow(new WindowConfig(Size: (200, 100)));
        Window window = app.GetRequiredService<Window>();
        List<ResolutionChangedEventArgs> changes = [];
        window.ResolutionChanged += changes.Add;

        window.Size = (300, 200);
        int changesBeforeTheFrame = changes.Count;
        app.RunFrame();

        Assert.Multiple(() =>
        {
            Assert.That(window.Size, Is.EqualTo(new Size<uint>(300, 200)));
            Assert.That(window.RenderSizeInPixels, Is.EqualTo(new ShortSize(300, 200)));
            Assert.That(changesBeforeTheFrame, Is.Zero);
            Assert.That(changes.Select(change => (change.OldSize, change.NewSize)), Is.EqualTo(new[] { (new ShortSize(200, 100), new ShortSize(300, 200)) }));
        });
    }

    [Test]
    public void SetPosition_ReadsBack()
    {
        using IPixelyApp app = BuildWithWindow(new WindowConfig());
        Window window = app.GetRequiredService<Window>();

        window.Position = new Vector2Int(5, 6);

        Assert.That(window.Position, Is.EqualTo(new Vector2Int(5, 6)));
    }

    [Test]
    public void FullscreenBorderless_CoversTheDisplay_AndLeavingItRestoresTheWindow()
    {
        using IPixelyApp app = BuildWithWindow(new WindowConfig(Size: (300, 200), Position: WindowPosition.At(new Vector2Int(5, 6))));
        Window window = app.GetRequiredService<Window>();
        List<ResolutionChangedEventArgs> changes = [];
        window.ResolutionChanged += changes.Add;

        window.SetFullscreenBorderless(true);
        (Size<uint>, Vector2Int) fullscreen = (window.Size, window.Position);
        app.RunFrame();
        window.SetFullscreenBorderless(false);

        Assert.Multiple(() =>
        {
            Assert.That(fullscreen, Is.EqualTo((new Size<uint>(1024, 768), new Vector2Int(0, 0))));
            Assert.That(changes.Select(change => change.NewSize), Is.EqualTo(new[] { new ShortSize(1024, 768) }));
            Assert.That((window.Size, window.Position), Is.EqualTo((new Size<uint>(300, 200), new Vector2Int(5, 6))));
        });
    }

    [Test]
    public void MouseGrabAndRelativeMouseMode_ReadBack()
    {
        using IPixelyApp app = BuildWithWindow(new WindowConfig());
        Window window = app.GetRequiredService<Window>();
        (bool, bool) initially = (window.MouseGrab, window.WindowRelativeMouseMode);

        window.MouseGrab = true;
        window.WindowRelativeMouseMode = true;

        Assert.Multiple(() =>
        {
            Assert.That(initially, Is.EqualTo((false, false)));
            Assert.That((window.MouseGrab, window.WindowRelativeMouseMode), Is.EqualTo((true, true)));
        });
    }

    [Test]
    public void SetShape_OnATransparentWindow_Succeeds()
    {
        using IPixelyApp app = BuildWithWindow(new WindowConfig(Transparent: true));
        Window window = app.GetRequiredService<Window>();

        Assert.Multiple(() =>
        {
            Assert.DoesNotThrow(() => window.SetShape(new BitMask(window.Size)));
            Assert.DoesNotThrow(() => window.SetShape(null));
        });
    }

    [Test]
    public void SetShape_OnAnOpaqueWindow_Throws()
    {
        using IPixelyApp app = BuildWithWindow(new WindowConfig());
        Window window = app.GetRequiredService<Window>();

        Assert.That(() => window.SetShape(new BitMask(window.Size)), Throws.TypeOf<PixelyException>());
    }

    [Test]
    public void AlwaysOnTopAndDragging_KeepWhatWasSet()
    {
        using IPixelyApp app = BuildWithWindow(new WindowConfig());
        Window window = app.GetRequiredService<Window>();
        bool initially = window.AlwaysOnTop;

        window.AlwaysOnTop = true;
        window.Draggable = true;
        bool draggable = window.Draggable;
        window.SetHitTest(static _ => HitTestResult.Draggable);

        Assert.That((initially, window.AlwaysOnTop, draggable, window.Draggable), Is.EqualTo((false, true, true, false)));
    }

    [Test]
    public void FileDialogs_Fail()
    {
        using IPixelyApp app = BuildWithWindow(new WindowConfig());
        Window window = app.GetRequiredService<Window>();

        Assert.Multiple(() =>
        {
            Assert.That(window.ShowModalOpenFileDialog().Status, Is.EqualTo(FileDialogStatus.Error));
            Assert.That(window.ShowModalSaveFileDialog().Status, Is.EqualTo(FileDialogStatus.Error));
        });
    }

    [Test]
    public void ShowModalMessageBox_Throws()
    {
        using IPixelyApp app = BuildWithWindow(new WindowConfig());
        Window window = app.GetRequiredService<Window>();

        Assert.That(() => window.ShowModalMessageBox(MessageBoxSeverity.Information, "Title", "Message"), Throws.TypeOf<PixelyException>());
    }

    [Test]
    public void Display_IsTheOnlyDisplay()
    {
        using IPixelyApp app = BuildWithWindow(new WindowConfig());
        Window window = app.GetRequiredService<Window>();
        IDisplayService displays = app.GetRequiredService<IDisplayService>();

        Display display = window.Display;

        Assert.Multiple(() =>
        {
            Assert.That(display, Is.EqualTo(displays.GetPrimaryDisplay()));
            Assert.That(displays.GetDisplays(), Is.EqualTo(new[] { display }));
            Assert.That((display.Bounds, display.UsableBounds), Is.EqualTo((new Rectangle(0, 0, 1024, 768), new Rectangle(0, 0, 1024, 768))));
            Assert.That((display.PixelDensity, display.ContentScale), Is.EqualTo((1f, 1f)));
        });
    }

    private static IPixelyApp BuildWithWindow(WindowConfig config)
    {
        PixelyAppBuilder builder = TestApp.CreateBuilder();
        builder.AddWindow(config);
        return builder.Build();
    }
}
