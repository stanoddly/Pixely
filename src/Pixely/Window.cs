using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Pixely.Content;
using Pixely.Gpu;
using Pixely.Utilities;
using XdlSharp;
using static XdlSharp.Xdl;

namespace Pixely;

public readonly record struct ResolutionChangedEventArgs(ShortSize OldSize, ShortSize NewSize, ulong Timestamp);

public delegate void ResolutionChangedHandler(ResolutionChangedEventArgs eventArgs);

public readonly record struct ScaleChangedEventArgs(float PixelDensity, float DisplayScale, ulong Timestamp);

public delegate void ScaleChangedHandler(ScaleChangedEventArgs eventArgs);

/// <summary>
/// An SDL window: its events, size, input and dialogs. How its frames reach the screen is up to the derived window:
/// <see cref="SwapchainWindow"/> presents them through a swapchain. Headless mode (Pixely.Headless) renders them into a texture
/// with an <c>OffscreenWindow</c>, and the stub GPU device of Pixely.Testing has a window of its own.
/// </summary>
public abstract class Window : IDisposable
{
    internal Pointer<SDL_Window> SdlWindow { get; private set; }
    internal uint SdlId { get; }
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

    internal Window(
        ViewScope viewScope,
        Pointer<SDL_Window> sdlWindow,
        uint sdlId,
        PixelyFrameClock frameClock,
        PlatformInfo platformInfo,
        WindowCloseBehavior closeBehavior)
    {
        ViewScope = viewScope;
        SdlWindow = sdlWindow;
        SdlId = sdlId;
        _frameClock = frameClock;
        _platformInfo = platformInfo;
        CloseBehavior = closeBehavior;
        _lastSize = RenderSizeInPixels;
        _lastPixelDensity = PixelDensity;
        _lastDisplayScale = DisplayScale;
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
    public float PixelDensity
    {
        get
        {
            unsafe
            {
                return SDL_GetWindowPixelDensity(SdlWindow);
            }
        }
    }

    /// <summary>
    /// How many render pixels the user wants a unit of content to take, such as a 1 px line. On a 200 % display it is 2 on Windows
    /// and X11, and on macOS, Wayland and in the browser for a window with high pixel density; without it, those report 1, since the
    /// compositor scales the window instead.
    /// </summary>
    public virtual float DisplayScale
    {
        get
        {
            unsafe
            {
                return SDL_GetWindowDisplayScale(SdlWindow);
            }
        }
    }

    /// <summary>The display SDL says the window is on: usually the one holding its centre. Main thread only.</summary>
    public Display Display
    {
        get
        {
            DisplayService.ThrowIfNotMainThread();
            SDL_DisplayID displayId;
            unsafe
            {
                displayId = SDL_GetDisplayForWindow(SdlWindow);
            }

            if (displayId == 0)
            {
                throw new PixelyException($"SDL_GetDisplayForWindow failed: {SdlError.GetError()}");
            }

            return DisplayService.Read(displayId);
        }
    }

    /// <summary>
    /// Waits until the platform has applied the window's pending changes, such as a new <see cref="Size"/> or
    /// <see cref="Position"/>. X11 and Wayland apply them later, so reading them right after setting them can return the old values.
    /// Returns false when the wait timed out; the changes may still arrive later.
    /// </summary>
    public bool Sync()
    {
        unsafe
        {
            return SDL_SyncWindow(SdlWindow);
        }
    }

    public ShortSize RenderSizeInPixels
    {
        get
        {
            int width, height;
            unsafe
            {
                SDL_GetWindowSizeInPixels(SdlWindow, &width, &height);
            }

            return new ShortSize((ushort)width, (ushort)height);
        }
    }

    public Size<uint> Size
    {
        get
        {
            int width, height;
            unsafe
            {
                SDL_GetWindowSize(SdlWindow, &width, &height);
            }
            return new Size<uint>((uint)width, (uint)height);
        }
        set
        {
            unsafe
            {
                SDL_SetWindowSize(SdlWindow, (int)value.Width, (int)value.Height);
            }
        }
    }

    public abstract TextureFormat ColorTargetFormat { get; }

    public bool MouseGrab
    {
        get
        {
            unsafe
            {
                return SDL_GetWindowMouseGrab(SdlWindow);
            }
        }
        set
        {
            unsafe
            {
                SDL_SetWindowMouseGrab(SdlWindow, value);
            }
        }
    }

    public bool WindowRelativeMouseMode
    {
        get
        {
            unsafe
            {
                return SDL_GetWindowRelativeMouseMode(SdlWindow);
            }
        }
        set
        {
            unsafe
            {
                SDL_SetWindowRelativeMouseMode(SdlWindow, value);
            }
        }
    }

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

    public bool AlwaysOnTop
    {
        get
        {
            unsafe
            {
                return (SDL_GetWindowFlags(SdlWindow) & SDL_WindowFlags.SDL_WINDOW_ALWAYS_ON_TOP) != 0;
            }
        }
        set
        {
            unsafe
            {
                if (SDL_SetWindowAlwaysOnTop(SdlWindow, value) == false)
                {
                    throw new PixelyException($"SDL_SetWindowAlwaysOnTop failed: {SdlError.GetError()}");
                }
            }
        }
    }

    /// <summary>Whether the window is showing on the desktop.</summary>
    public bool IsVisible
    {
        get
        {
            unsafe
            {
                return (SDL_GetWindowFlags(SdlWindow) & SDL_WindowFlags.SDL_WINDOW_HIDDEN) == 0;
            }
        }
    }

    // Whether the frames go to a texture instead of the screen, as a headless app's do (Pixely.Headless); the diagnostics report it.
    internal virtual bool IsOffscreen => false;

    /// <summary>
    /// Whether frames rendered to the window can be seen right now. The render coordinator skips its renderers,
    /// and update systems that only work for the renderer skip their frame, when this is false. By default a
    /// hidden or minimized window is not renderable; a window that draws elsewhere overrides it.
    /// </summary>
    public virtual bool IsRenderable
    {
        get
        {
            unsafe
            {
                return (SDL_GetWindowFlags(SdlWindow) & (SDL_WindowFlags.SDL_WINDOW_HIDDEN | SDL_WindowFlags.SDL_WINDOW_MINIMIZED)) == 0;
            }
        }
    }

    public virtual bool Show()
    {
        unsafe
        {
            return SDL_ShowWindow(SdlWindow);
        }
    }

    public bool Hide()
    {
        unsafe
        {
            return SDL_HideWindow(SdlWindow);
        }
    }

    public bool Raise()
    {
        unsafe
        {
            return SDL_RaiseWindow(SdlWindow);
        }
    }

    private bool _draggable;
    private GCHandle _hitTestHandle;
    private Func<Vector2Int, HitTestResult>? _hitTestCallback;

    public bool Draggable
    {
        get => _draggable;
        set
        {
            unsafe
            {
                if (value)
                {
                    if (!SDL_SetWindowHitTest(SdlWindow, &HitTestDraggable, IntPtr.Zero))
                    {
                        throw new PixelyException($"SDL_SetWindowHitTest failed: {SdlError.GetError()}");
                    }
                }
                else
                {
                    if (!SDL_SetWindowHitTest(SdlWindow, null, IntPtr.Zero))
                    {
                        throw new PixelyException($"SDL_SetWindowHitTest failed: {SdlError.GetError()}");
                    }
                }
            }
            ClearHitTestCallback();
            _draggable = value;
        }
    }

    public void SetHitTest(Func<Vector2Int, HitTestResult>? callback)
    {
        if (callback == null)
        {
            unsafe
            {
                if (!SDL_SetWindowHitTest(SdlWindow, null, IntPtr.Zero))
                {
                    throw new PixelyException($"SDL_SetWindowHitTest failed: {SdlError.GetError()}");
                }
            }
            ClearHitTestCallback();
            _draggable = false;
            return;
        }

        _hitTestCallback = callback;
        if (!_hitTestHandle.IsAllocated)
        {
            _hitTestHandle = GCHandle.Alloc(this);
        }
        unsafe
        {
            if (!SDL_SetWindowHitTest(SdlWindow, &HitTestCallback, GCHandle.ToIntPtr(_hitTestHandle)))
            {
                throw new PixelyException($"SDL_SetWindowHitTest failed: {SdlError.GetError()}");
            }
        }
        _draggable = false;
    }

    public void SetShape(BitMask? shape)
    {
        if (!SupportsWindowShape)
        {
            throw new PlatformNotSupportedException("Window shapes are not supported by the current SDL video driver.");
        }

        unsafe
        {
            if (shape == null)
            {
                if (!SDL_SetWindowShape(SdlWindow, null))
                {
                    throw new PixelyException($"SDL_SetWindowShape failed: {SdlError.GetError()}");
                }
                return;
            }

            Size<uint> size = Size;
            if (shape.Size != size)
            {
                throw new ArgumentException($"Shape size {shape.Size.Width}x{shape.Size.Height} does not match window size {size.Width}x{size.Height}.", nameof(shape));
            }

            int width = checked((int)size.Width);
            int height = checked((int)size.Height);
            Pointer<SDL_Surface> surface = SDL_CreateSurface(width, height, SDL_PixelFormat.SDL_PIXELFORMAT_ARGB8888);
            if (surface.IsNull)
            {
                throw new PixelyException($"SDL_CreateSurface failed: {SdlError.GetError()}");
            }

            try
            {
                SDL_Surface* sdlSurface = surface;
                uint* pixels = (uint*)sdlSurface->pixels.ToPointer();
                int stride = sdlSurface->pitch / sizeof(uint);

                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        pixels[y * stride + x] = shape.GetUnchecked(y * width + x) ? 0xFF000000u : 0x00000000u;
                    }
                }

                if (!SDL_SetWindowShape(SdlWindow, surface))
                {
                    throw new PixelyException($"SDL_SetWindowShape failed: {SdlError.GetError()}");
                }
            }
            finally
            {
                SDL_DestroySurface(surface);
            }
        }
    }

    private void ClearHitTestCallback()
    {
        if (_hitTestHandle.IsAllocated)
        {
            _hitTestHandle.Free();
        }
        _hitTestCallback = null;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static unsafe SDL_HitTestResult HitTestDraggable(SDL_Window* window, SDL_Point* area, IntPtr data)
    {
        return SDL_HitTestResult.SDL_HITTEST_DRAGGABLE;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static unsafe SDL_HitTestResult HitTestCallback(SDL_Window* window, SDL_Point* area, IntPtr data)
    {
        GCHandle handle = GCHandle.FromIntPtr(data);
        Window self = (Window)handle.Target!;
        return self._hitTestCallback!(new Vector2Int(area->x, area->y)) == HitTestResult.Draggable
            ? SDL_HitTestResult.SDL_HITTEST_DRAGGABLE
            : SDL_HitTestResult.SDL_HITTEST_NORMAL;
    }

    public Vector2Int Position
    {
        get
        {
            int x, y;
            unsafe
            {
                SDL_GetWindowPosition(SdlWindow, &x, &y);
            }
            return new Vector2Int(x, y);
        }
        set
        {
            unsafe
            {
                SDL_SetWindowPosition(SdlWindow, value.X, value.Y);
            }
        }
    }

    public abstract bool TryWaitAndAcquireSwapchainTexture(CommandBuffer commandBuffer, out SwapchainTexture swapchainTexture);

    public void SetFullscreenBorderless(bool fullscreen)
    {
        unsafe
        {
            SDL_SetWindowFullscreen(SdlWindow, fullscreen);
        }
    }

    public bool SetIcon(Image icon)
    {
        ReadOnlySpan<byte> pixelData = icon.Data;
        int width = icon.Size.Width;
        int height = icon.Size.Height;
        int pitch = ImageSurfaces.GetPackedPitch(icon);

        unsafe
        {
            fixed (byte* pixels = pixelData)
            {
                Pointer<SDL_Surface> surface = SDL_CreateSurfaceFrom(
                    width,
                    height,
                    (SDL_PixelFormat)icon.PixelFormat,
                    (IntPtr)pixels,
                    pitch);

                if (surface.IsNull)
                {
                    return false;
                }

                try
                {
                    return SDL_SetWindowIcon(SdlWindow, surface);
                }
                finally
                {
                    SDL_DestroySurface(surface);
                }
            }
        }
    }

    public unsafe void ShowModalMessageBox(MessageBoxSeverity severity, string title, string message)
    {
        _frameClock.Pause();
        try
        {
            MessageBox.Show(severity, title, message, SdlWindow);
        }
        finally
        {
            _frameClock.Resume();
        }
    }

    public FileDialogResult ShowModalOpenFileDialog(IReadOnlyList<FileDialogFilter>? filters = null, string? defaultLocation = null, bool allowMany = false)
    {
        _frameClock.Pause();
        try
        {
            return ShowModalFileDialog(filters ?? Array.Empty<FileDialogFilter>(), defaultLocation, allowMany, FileDialogKind.Open);
        }
        finally
        {
            _frameClock.Resume();
        }
    }

    public FileDialogResult ShowModalSaveFileDialog(IReadOnlyList<FileDialogFilter>? filters = null, string? defaultLocation = null)
    {
        _frameClock.Pause();
        try
        {
            return ShowModalFileDialog(filters ?? Array.Empty<FileDialogFilter>(), defaultLocation, false, FileDialogKind.Save);
        }
        finally
        {
            _frameClock.Resume();
        }
    }

    private unsafe FileDialogResult ShowModalFileDialog(
        IReadOnlyList<FileDialogFilter> filters,
        string? defaultLocation,
        bool allowMany,
        FileDialogKind kind)
    {
        ArgumentNullException.ThrowIfNull(filters);

        if (!SDL_IsMainThread())
        {
            throw new PixelyException("File dialogs must be shown from the main thread.");
        }

        ModalFileDialogState state = new();
        GCHandle stateHandle = GCHandle.Alloc(state);
        IntPtr userdata = GCHandle.ToIntPtr(stateHandle);
        NativeFileDialogFilters nativeFilters = new(filters);
        using NullTerminatedUtf8 utf8DefaultLocation = new NullTerminatedUtf8(defaultLocation);

        try
        {
            fixed (SDL_DialogFileFilter* filtersPointer = nativeFilters.Filters)
            {
                SDL_DialogFileFilter* actualFiltersPointer = nativeFilters.Filters.Length == 0 ? null : filtersPointer;

                if (kind == FileDialogKind.Open)
                {
                    SDL_ShowOpenFileDialog(
                        &OnFileDialogCompleted,
                        userdata,
                        SdlWindow,
                        actualFiltersPointer,
                        nativeFilters.Filters.Length,
                        utf8DefaultLocation.Span,
                        allowMany);
                }
                else
                {
                    SDL_ShowSaveFileDialog(
                        &OnFileDialogCompleted,
                        userdata,
                        SdlWindow,
                        actualFiltersPointer,
                        nativeFilters.Filters.Length,
                        utf8DefaultLocation.Span);
                }

                while (!state.IsCompleted)
                {
                    SDL_PumpEvents();
                    SDL_Delay(10);
                }
            }

            return state.GetResult();
        }
        finally
        {
            stateHandle.Free();
            nativeFilters.Dispose();
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static unsafe void OnFileDialogCompleted(IntPtr userdata, byte** fileList, int filter)
    {
        GCHandle stateHandle = GCHandle.FromIntPtr(userdata);
        ModalFileDialogState state = (ModalFileDialogState)stateHandle.Target!;
        state.Complete(CreateFileDialogResult(fileList));
    }

    private static unsafe FileDialogResult CreateFileDialogResult(byte** fileList)
    {
        if (fileList == null)
        {
            string? error = SdlError.GetError();
            return FileDialogResult.Failed(string.IsNullOrWhiteSpace(error) ? "File dialog failed." : error);
        }

        List<string> paths = new();
        for (int i = 0; fileList[i] != null; i++)
        {
            string? path = Marshal.PtrToStringUTF8((IntPtr)fileList[i]);
            if (path != null)
            {
                paths.Add(path);
            }
        }

        if (paths.Count == 0)
        {
            return FileDialogResult.Canceled();
        }

        return FileDialogResult.Accepted(paths);
    }

    public override int GetHashCode()
    {
        return ViewScope.GetHashCode();
    }

    public virtual void Dispose()
    {
        ClearHitTestCallback();
        unsafe
        {
            SDL_DestroyWindow(SdlWindow);
            SdlWindow = null;
        }
    }

    private sealed class ModalFileDialogState
    {
        private readonly ManualResetEventSlim _completed = new();
        private readonly object _lock = new();
        private FileDialogResult _result = FileDialogResult.Failed("File dialog did not complete.");

        public bool IsCompleted
        {
            get
            {
                return _completed.IsSet;
            }
        }

        public void Complete(FileDialogResult result)
        {
            lock (_lock)
            {
                _result = result;
            }

            _completed.Set();
        }

        public FileDialogResult GetResult()
        {
            lock (_lock)
            {
                return _result;
            }
        }
    }

    private enum FileDialogKind
    {
        Open,
        Save
    }

    private sealed class NativeFileDialogFilters : IDisposable
    {
        private readonly List<IntPtr> _allocatedStrings = new();

        public SDL_DialogFileFilter[] Filters { get; }

        public unsafe NativeFileDialogFilters(IReadOnlyList<FileDialogFilter> filters)
        {
            ArgumentNullException.ThrowIfNull(filters);

            Filters = new SDL_DialogFileFilter[filters.Count];

            for (int i = 0; i < filters.Count; i++)
            {
                if (filters[i].Name == null)
                {
                    throw new ArgumentException("Filter name cannot be null.", nameof(filters));
                }

                if (filters[i].Pattern == null)
                {
                    throw new ArgumentException("Filter pattern cannot be null.", nameof(filters));
                }

                IntPtr namePointer = Marshal.StringToCoTaskMemUTF8(filters[i].Name);
                IntPtr patternPointer = Marshal.StringToCoTaskMemUTF8(filters[i].Pattern);
                _allocatedStrings.Add(namePointer);
                _allocatedStrings.Add(patternPointer);

                Filters[i] = new SDL_DialogFileFilter
                {
                    name = (byte*)namePointer,
                    pattern = (byte*)patternPointer
                };
            }
        }

        public void Dispose()
        {
            foreach (IntPtr allocatedString in _allocatedStrings)
            {
                Marshal.FreeCoTaskMem(allocatedString);
            }
        }
    }
}
