using Pixely.App;
using Pixely.Content;
using Pixely.DependencyInjection;
using Pixely.Gpu;
using Pixely.Input;
using SDL;

namespace Pixely.Tests;

// Builds real apps, which initializes SDL video with the dummy driver. Its cursors are SDL's own, so the tests cover validation
// and the calls into SDL, not what a desktop shows.
[NonParallelizable]
public class CursorServiceTests
{
    private IPixelyApp _app = null!;
    private ICursorService _cursorService = null!;

    [SetUp]
    public void SetUp()
    {
        SDL3.SDL_SetHintWithPriority(SDL3.SDL_HINT_VIDEO_DRIVER, "dummy", SDL_HintPriority.SDL_HINT_DEFAULT);
        _app = new PixelyAppBuilder().Build();
        _cursorService = ((PixelyApp)_app).ServiceProvider.GetRequiredService<ICursorService>();
    }

    [TearDown]
    public void TearDown()
    {
        _app.Dispose();
    }

    [Test]
    public void SetCursor_ImageCursor_ThenResetCursor_Succeeds()
    {
        using Cursor cursor = _cursorService.CreateCursor(CreateImage(2, 2), new Vector2Int(1, 1));

        Assert.DoesNotThrow(() =>
        {
            _cursorService.SetCursor(cursor);
            _cursorService.ResetCursor();
        });
    }

    // The dummy driver has no system cursors, so SDL fails to create one.
    [Test]
    public void CreateSystemCursor_VideoDriverWithoutSystemCursors_Throws()
    {
        Assert.Throws<PixelyException>(() => _cursorService.CreateSystemCursor(SystemCursor.Pointer));
    }

    [Test]
    public void SetCursor_AnimatedCursor_Succeeds()
    {
        CursorFrame[] frames = [new(CreateImage(2, 2), TimeSpan.FromMilliseconds(100)), new(CreateImage(2, 2, PixelFormat.Argb8888), TimeSpan.FromMilliseconds(100))];
        using Cursor cursor = _cursorService.CreateCursor(frames, Vector2Int.Zero);

        Assert.DoesNotThrow(() => _cursorService.SetCursor(cursor));
    }

    [Test]
    public void IsCursorVisible_Set_ChangesVisibility()
    {
        _cursorService.IsCursorVisible = false;
        bool hidden = _cursorService.IsCursorVisible;
        _cursorService.IsCursorVisible = true;

        Assert.Multiple(() =>
        {
            Assert.That(hidden, Is.False);
            Assert.That(_cursorService.IsCursorVisible, Is.True);
        });
    }

    [Test]
    public void CreateSystemCursor_UnknownShape_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _cursorService.CreateSystemCursor((SystemCursor)int.MaxValue));
    }

    [Test]
    public void CreateCursor_NoFrames_Throws()
    {
        Assert.Throws<ArgumentException>(() => _cursorService.CreateCursor(ReadOnlySpan<CursorFrame>.Empty, Vector2Int.Zero));
    }

    [Test]
    public void CreateCursor_FrameWithoutImage_Throws()
    {
        CursorFrame[] frames = [new(CreateImage(2, 2), TimeSpan.Zero), default];

        Assert.Throws<ArgumentException>(() => _cursorService.CreateCursor(frames, Vector2Int.Zero));
    }

    [Test]
    public void CreateCursor_FramesOfDifferentSizes_Throws()
    {
        CursorFrame[] frames = [new(CreateImage(2, 2), TimeSpan.Zero), new(CreateImage(3, 2), TimeSpan.Zero)];

        Assert.Throws<ArgumentException>(() => _cursorService.CreateCursor(frames, Vector2Int.Zero));
    }

    [TestCase(-1, 0)]
    [TestCase(0, -1)]
    [TestCase(2, 0)]
    [TestCase(0, 2)]
    public void CreateCursor_HotspotOutsideImage_Throws(int x, int y)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _cursorService.CreateCursor(CreateImage(2, 2), new Vector2Int(x, y)));
    }

    [Test]
    public void CreateCursor_NegativeDuration_Throws()
    {
        CursorFrame[] frames = [new(CreateImage(2, 2), TimeSpan.FromMilliseconds(-1))];

        Assert.Throws<ArgumentOutOfRangeException>(() => _cursorService.CreateCursor(frames, Vector2Int.Zero));
    }

    [Test]
    public void CreateCursor_IndexedImage_Throws()
    {
        RawImage image = new(new byte[4], new ShortSize(2, 2), PixelFormat.Index8);

        Assert.Throws<NotSupportedException>(() => _cursorService.CreateCursor(image, Vector2Int.Zero));
    }

    [Test]
    public void CreateCursor_ImageWithTooFewBytes_Throws()
    {
        RawImage image = new(new byte[4], new ShortSize(2, 2), PixelFormat.Abgr8888);

        Assert.Throws<NotSupportedException>(() => _cursorService.CreateCursor(image, Vector2Int.Zero));
    }

    [Test]
    public void CreateCursor_OffTheMainThread_Throws()
    {
        Assert.Throws<PixelyException>(() => Task.Run(() => _cursorService.CreateCursor(CreateImage(2, 2), Vector2Int.Zero)).GetAwaiter().GetResult());
    }

    [Test]
    public void SetCursor_DisposedCursor_Throws()
    {
        Cursor cursor = _cursorService.CreateCursor(CreateImage(2, 2), Vector2Int.Zero);
        cursor.Dispose();

        Assert.Throws<ObjectDisposedException>(() => _cursorService.SetCursor(cursor));
    }

    [Test]
    public void Dispose_ActiveCursorTwice_DoesNotThrow()
    {
        Cursor cursor = _cursorService.CreateCursor(CreateImage(2, 2), Vector2Int.Zero);
        _cursorService.SetCursor(cursor);

        Assert.DoesNotThrow(() =>
        {
            cursor.Dispose();
            cursor.Dispose();
        });
    }

    [Test]
    public void Dispose_CursorAfterTheApp_DoesNotThrow()
    {
        Cursor cursor = _cursorService.CreateCursor(CreateImage(2, 2), Vector2Int.Zero);
        _app.Dispose();

        Assert.Multiple(() =>
        {
            Assert.DoesNotThrow(cursor.Dispose);
            Assert.Throws<ObjectDisposedException>(() => _cursorService.SetCursor(cursor));
        });
    }

    [TestCase(0, 0u)]
    [TestCase(1, 1u)]
    [TestCase(9_999, 1u)]
    [TestCase(10_000, 1u)]
    [TestCase(10_001, 2u)]
    public void ToMilliseconds_RoundsUp(long ticks, uint expected)
    {
        Assert.That(CursorService.ToMilliseconds(TimeSpan.FromTicks(ticks), 0), Is.EqualTo(expected));
    }

    [Test]
    public void ToMilliseconds_AboveUIntMilliseconds_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CursorService.ToMilliseconds(TimeSpan.FromMilliseconds(uint.MaxValue + 1L), 0));
    }

    private static RawImage CreateImage(ushort width, ushort height, PixelFormat pixelFormat = PixelFormat.Abgr8888) =>
        new(new byte[width * height * 4], new ShortSize(width, height), pixelFormat);
}
