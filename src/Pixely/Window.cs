using Pixely.Content;
using Pixely.Gpu;

namespace Pixely;

public readonly record struct ResolutionChangedEventArgs(ShortSize OldSize, ShortSize NewSize, ulong Timestamp);

public delegate void ResolutionChangedHandler(ResolutionChangedEventArgs eventArgs);

public readonly record struct ScaleChangedEventArgs(float PixelDensity, float DisplayScale, ulong Timestamp);

public delegate void ScaleChangedHandler(ScaleChangedEventArgs eventArgs);

/// <summary>
/// A window: its events, size, input and dialogs. <see cref="NativeWindow"/> is a window of the platform, through SDL, and
/// presents its frames as its derived window does: <see cref="SwapchainWindow"/> through a swapchain, headless mode's
/// <c>OffscreenWindow</c> (Pixely.Headless) into a texture. Pixely.Testing's <c>StubWindow</c> is a window without a platform.
/// </summary>
public abstract class Window : IDisposable
{
    internal WindowCloseBehavior CloseBehavior { get; }
    private readonly PixelyFrameClock _frameClock;
    private readonly PlatformInfo _platformInfo;

    internal ViewScope ViewScope { get; }

    private ShortSize _lastSize;
    private float _lastPixelDensity;
    private float _lastDisplayScale;

    public event ResolutionChangedHandler? ResolutionChanged;

    // Raised when PixelDensity or DisplayScale changes, such as when the window moves to a display with another scale.
    public event ScaleChangedHandler? ScaleChanged;

    // The initial values are what the derived window's getters return at this point. The constructor calls no virtual member,
    // because the derived window's fields are not set yet.
    internal Window(
        ViewScope viewScope,
        PixelyFrameClock frameClock,
        PlatformInfo platformInfo,
        WindowCloseBehavior closeBehavior,
        ShortSize initialRenderSize,
        float initialPixelDensity,
        float initialDisplayScale)
    {
        ViewScope = viewScope;
        _frameClock = frameClock;
        _platformInfo = platformInfo;
        CloseBehavior = closeBehavior;
        _lastSize = initialRenderSize;
        _lastPixelDensity = initialPixelDensity;
        _lastDisplayScale = initialDisplayScale;
    }

    internal void OnPixelSizeChanged(ulong timestamp)
    {
        ShortSize newSize = RenderSizeInPixels;
        ShortSize oldSize = _lastSize;

        if (newSize == oldSize) return;

        _lastSize = newSize;
        ResolutionChanged?.Invoke(new ResolutionChangedEventArgs(oldSize, newSize, timestamp));
    }

    // SDL raises SDL_EVENT_WINDOW_DISPLAY_SCALE_CHANGED when the display scale changes. A pixel density change comes with a pixel size
    // change, so SDL_EVENT_WINDOW_PIXEL_SIZE_CHANGED checks as well.
    internal void OnScaleChanged(ulong timestamp)
    {
        float pixelDensity = PixelDensity;
        float displayScale = DisplayScale;
        if (pixelDensity == _lastPixelDensity && displayScale == _lastDisplayScale)
        {
            return;
        }

        _lastPixelDensity = pixelDensity;
        _lastDisplayScale = displayScale;
        ScaleChanged?.Invoke(new ScaleChangedEventArgs(pixelDensity, displayScale, timestamp));
    }

    /// <summary>
    /// Render pixels per unit of <see cref="Size"/>: <see cref="RenderSizeInPixels"/> divided by <see cref="Size"/>. It is 2 for a
    /// window with <see cref="WindowConfig.HighPixelDensity"/> on a 200 % display on macOS, Wayland and in the browser, and 1 without
    /// it. Windows and X11 size windows in pixels, so it is 1 there.
    /// </summary>
    public abstract float PixelDensity { get; }

    /// <summary>
    /// How many render pixels the user wants a unit of content to take, such as a 1 px line. On a 200 % display it is 2 on Windows
    /// and X11, and on macOS, Wayland and in the browser for a window with high pixel density; without it, those report 1, since the
    /// compositor scales the window instead.
    /// </summary>
    public abstract float DisplayScale { get; }

    /// <summary>The display SDL says the window is on: usually the one holding its centre. Main thread only.</summary>
    public abstract Display Display { get; }

    /// <summary>
    /// Waits until the platform has applied the window's pending changes, such as a new <see cref="Size"/> or
    /// <see cref="Position"/>. X11 and Wayland apply them later, so reading them right after setting them can return the old values.
    /// Returns false when the wait timed out; the changes may still arrive later.
    /// </summary>
    public abstract bool Sync();

    public abstract ShortSize RenderSizeInPixels { get; }

    public abstract Size<uint> Size { get; set; }

    public abstract TextureFormat ColorTargetFormat { get; }

    public abstract bool MouseGrab { get; set; }

    public abstract bool WindowRelativeMouseMode { get; set; }

    public bool SupportsAlwaysOnTop
    {
        get
        {
            return _platformInfo.SupportsAlwaysOnTopWindows;
        }
    }

    public bool SupportsWindowShape
    {
        get
        {
            return _platformInfo.SupportsWindowShape;
        }
    }

    public bool SupportsSetWindowPosition
    {
        get
        {
            return _platformInfo.SupportsSetWindowPosition;
        }
    }

    public abstract bool AlwaysOnTop { get; set; }

    /// <summary>Whether the window is showing on the desktop.</summary>
    public abstract bool IsVisible { get; }

    // Whether the frames go to a texture instead of the screen, as a headless app's do (Pixely.Headless); the diagnostics report it.
    internal virtual bool IsOffscreen => false;

    /// <summary>
    /// Whether frames rendered to the window can be seen right now. The render coordinator skips its renderers,
    /// and update systems that only work for the renderer skip their frame, when this is false. A hidden or minimized
    /// window is not renderable; a window that draws elsewhere is.
    /// </summary>
    public abstract bool IsRenderable { get; }

    public abstract bool Show();

    public abstract bool Hide();

    public abstract bool Raise();

    /// <summary>
    /// Minimises the window, as its minimise button would, and returns false where SDL cannot, such as in the browser. On Wayland it
    /// returns true but does nothing when the compositor does not offer minimising. A hidden window is minimised when it is shown.
    /// </summary>
    public abstract bool Minimize();

    public abstract bool Draggable { get; set; }

    public abstract void SetHitTest(Func<Vector2Int, HitTestResult>? callback);

    public void SetShape(BitMask? shape)
    {
        if (!SupportsWindowShape)
        {
            throw new PlatformNotSupportedException("Window shapes are not supported by the current SDL video driver.");
        }

        if (shape != null)
        {
            Size<uint> size = Size;
            if (shape.Size != size)
            {
                throw new ArgumentException($"Shape size {shape.Size.Width}x{shape.Size.Height} does not match window size {size.Width}x{size.Height}.", nameof(shape));
            }
        }

        SetShapeCore(shape);
    }

    // A non-null shape has the window's size.
    internal abstract void SetShapeCore(BitMask? shape);

    public abstract Vector2Int Position { get; set; }

    public abstract bool TryWaitAndAcquireSwapchainTexture(CommandBuffer commandBuffer, out SwapchainTexture swapchainTexture);

    public abstract void SetFullscreenBorderless(bool fullscreen);

    public abstract bool SetIcon(Image icon);

    public void ShowModalMessageBox(MessageBoxSeverity severity, string title, string message)
    {
        _frameClock.Pause();
        try
        {
            ShowModalMessageBoxCore(severity, title, message);
        }
        finally
        {
            _frameClock.Resume();
        }
    }

    public FileDialogResult ShowModalOpenFileDialog(IReadOnlyList<FileDialogFilter>? filters = null, string? defaultLocation = null, bool allowMany = false)
    {
        ThrowIfInvalid(filters);
        _frameClock.Pause();
        try
        {
            return ShowModalOpenFileDialogCore(filters ?? Array.Empty<FileDialogFilter>(), defaultLocation, allowMany);
        }
        finally
        {
            _frameClock.Resume();
        }
    }

    public FileDialogResult ShowModalSaveFileDialog(IReadOnlyList<FileDialogFilter>? filters = null, string? defaultLocation = null)
    {
        ThrowIfInvalid(filters);
        _frameClock.Pause();
        try
        {
            return ShowModalSaveFileDialogCore(filters ?? Array.Empty<FileDialogFilter>(), defaultLocation);
        }
        finally
        {
            _frameClock.Resume();
        }
    }

    private static void ThrowIfInvalid(IReadOnlyList<FileDialogFilter>? filters)
    {
        if (filters == null)
        {
            return;
        }

        foreach (FileDialogFilter filter in filters)
        {
            if (filter.Name == null)
            {
                throw new ArgumentException("Filter name cannot be null.", nameof(filters));
            }

            if (filter.Pattern == null)
            {
                throw new ArgumentException("Filter pattern cannot be null.", nameof(filters));
            }
        }
    }

    internal abstract void ShowModalMessageBoxCore(MessageBoxSeverity severity, string title, string message);

    internal abstract FileDialogResult ShowModalOpenFileDialogCore(IReadOnlyList<FileDialogFilter> filters, string? defaultLocation, bool allowMany);

    internal abstract FileDialogResult ShowModalSaveFileDialogCore(IReadOnlyList<FileDialogFilter> filters, string? defaultLocation);

    // Text input of the window, which ITextInputService starts and stops for the window's view.
    internal abstract bool IsTextInputActive { get; }

    internal abstract void StartTextInput();

    internal abstract void StopTextInput();

    public override int GetHashCode()
    {
        return ViewScope.GetHashCode();
    }

    public abstract void Dispose();
}
