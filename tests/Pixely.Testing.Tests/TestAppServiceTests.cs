using Pixely.App;
using Pixely.Content;
using Pixely.Gpu;
using Pixely.Input;

namespace Pixely.Testing.Tests;

// How the platform services of a test app behave, which a game's tests rely on.
public class TestAppServiceTests
{
    [Test]
    public void TextInput_StartsAndStopsForTheWindow()
    {
        using IPixelyApp app = BuildWithWindow();
        ITextInputService textInput = app.GetRequiredService<ITextInputService>();
        bool initially = textInput.IsActiveFor();

        textInput.Start();
        bool started = textInput.IsActiveFor();
        textInput.Stop();

        Assert.That((initially, started, textInput.IsActiveFor()), Is.EqualTo((false, true, false)));
    }

    [Test]
    public void Clipboard_StartsEmpty_AndKeepsTheTextSet()
    {
        using IPixelyApp app = BuildWithWindow();
        IClipboardService clipboard = app.GetRequiredService<IClipboardService>();
        (bool, string?) initially = (clipboard.HasText, clipboard.GetText());

        clipboard.SetText("copied");
        (bool, string?) set = (clipboard.HasText, clipboard.GetText());
        clipboard.SetText("");

        Assert.Multiple(() =>
        {
            Assert.That(initially, Is.EqualTo((false, "")));
            Assert.That(set, Is.EqualTo((true, "copied")));
            Assert.That((clipboard.HasText, clipboard.GetText()), Is.EqualTo((false, "")));
        });
    }

    [Test]
    public void PlatformSettings_ThemeIsUnknown_AndNoLocaleIsPreferred()
    {
        using IPixelyApp app = BuildWithWindow();
        IPlatformSettings platformSettings = app.GetRequiredService<IPlatformSettings>();

        Assert.That((platformSettings.Theme, platformSettings.GetPreferredLocales().Count), Is.EqualTo((SystemTheme.Unknown, 0)));
    }

    [Test]
    public void Cursor_IsVisible_AndHides()
    {
        using IPixelyApp app = BuildWithWindow();
        ICursorService cursors = app.GetRequiredService<ICursorService>();
        bool initially = cursors.IsCursorVisible;

        cursors.IsCursorVisible = false;

        Assert.That((initially, cursors.IsCursorVisible), Is.EqualTo((true, false)));
    }

    [Test]
    public void Cursor_FromAnImage_IsSetAndReset()
    {
        using IPixelyApp app = BuildWithWindow();
        ICursorService cursors = app.GetRequiredService<ICursorService>();
        using Cursor cursor = cursors.CreateCursor(new RawImage(new byte[4 * 4 * 4], new ShortSize(4, 4), PixelFormat.Abgr8888), new Vector2Int(1, 1));

        Assert.Multiple(() =>
        {
            Assert.DoesNotThrow(() => cursors.SetCursor(cursor));
            Assert.DoesNotThrow(cursors.ResetCursor);
        });
    }

    [Test]
    public void Cursor_System_IsSetAndReset()
    {
        using IPixelyApp app = BuildWithWindow();
        ICursorService cursors = app.GetRequiredService<ICursorService>();
        using Cursor cursor = cursors.CreateSystemCursor(SystemCursor.Pointer);

        Assert.Multiple(() =>
        {
            Assert.DoesNotThrow(() => cursors.SetCursor(cursor));
            Assert.DoesNotThrow(cursors.ResetCursor);
        });
    }

    [Test]
    public void Cursor_WithoutFramesOrWithAHotspotOutside_Throws()
    {
        using IPixelyApp app = BuildWithWindow();
        ICursorService cursors = app.GetRequiredService<ICursorService>();

        Assert.Multiple(() =>
        {
            Assert.That(() => cursors.CreateCursor(ReadOnlySpan<CursorFrame>.Empty, new Vector2Int(0, 0)), Throws.ArgumentException);
            Assert.That(() => cursors.CreateCursor(new RawImage(new byte[4 * 4 * 4], new ShortSize(4, 4), PixelFormat.Abgr8888), new Vector2Int(4, 0)), Throws.TypeOf<ArgumentOutOfRangeException>());
        });
    }

    [Test]
    public void Cursor_Disposed_CannotBeSet()
    {
        using IPixelyApp app = BuildWithWindow();
        ICursorService cursors = app.GetRequiredService<ICursorService>();
        Cursor cursor = cursors.CreateSystemCursor(SystemCursor.Pointer);

        cursor.Dispose();

        Assert.That(() => cursors.SetCursor(cursor), Throws.TypeOf<ObjectDisposedException>());
    }

    [Test]
    public void Mouse_GlobalState_IsAtTheOriginWithNoButtons()
    {
        using IPixelyApp app = BuildWithWindow();

        Assert.That(app.GetRequiredService<IMouseService>().GetGlobalState(), Is.EqualTo(new MouseState(System.Numerics.Vector2.Zero, 0)));
    }

    [Test]
    public void PlatformInfo_SupportsEveryWindowFeature()
    {
        using IPixelyApp app = BuildWithWindow();
        PlatformInfo platformInfo = app.GetRequiredService<PlatformInfo>();

        Assert.That((platformInfo.SupportsAlwaysOnTopWindows, platformInfo.SupportsSetWindowPosition, platformInfo.SupportsWindowShape), Is.EqualTo((true, true, true)));
    }

    private static IPixelyApp BuildWithWindow()
    {
        PixelyAppBuilder builder = TestApp.CreateBuilder();
        builder.AddWindow(new WindowConfig());
        return builder.Build();
    }
}
