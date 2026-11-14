using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Pixely.Content;
using Pixely.Gpu;
using Pixely.Utilities;
using XdlSharp;
using static XdlSharp.Xdl;

namespace Pixely;

/// <summary>
/// A window of the platform, through SDL. How its frames reach the screen is up to the derived window:
/// <see cref="SwapchainWindow"/> presents them through a swapchain, and headless mode (Pixely.Headless) renders them into a
/// texture with an <c>OffscreenWindow</c>.
/// </summary>
public abstract class NativeWindow : Window
{
    internal Pointer<SDL_Window> SdlWindow { get; private set; }
    internal uint SdlId { get; }

    // The derived window passes the display scale its DisplayScale getter returns at this point.
    internal NativeWindow(
        ViewScope viewScope,
        Pointer<SDL_Window> sdlWindow,
        uint sdlId,
        PixelyFrameClock frameClock,
        PlatformInfo platformInfo,
        WindowCloseBehavior closeBehavior,
        float initialDisplayScale)
        : base(viewScope, frameClock, platformInfo, closeBehavior, ReadRenderSizeInPixels(sdlWindow), ReadPixelDensity(sdlWindow), initialDisplayScale)
    {
        SdlWindow = sdlWindow;
        SdlId = sdlId;
    }

    private protected static float ReadDisplayScale(Pointer<SDL_Window> sdlWindow)
    {
        unsafe
        {
            return SDL_GetWindowDisplayScale(sdlWindow);
        }
    }

    private static float ReadPixelDensity(Pointer<SDL_Window> sdlWindow)
    {
        unsafe
        {
            return SDL_GetWindowPixelDensity(sdlWindow);
        }
    }

    private static ShortSize ReadRenderSizeInPixels(Pointer<SDL_Window> sdlWindow)
    {
        int width, height;
        unsafe
        {
            SDL_GetWindowSizeInPixels(sdlWindow, &width, &height);
        }

        return new ShortSize((ushort)width, (ushort)height);
    }

    public override float PixelDensity => ReadPixelDensity(SdlWindow);

    public override float DisplayScale => ReadDisplayScale(SdlWindow);

    public override Display Display
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

    public override bool Sync()
    {
        unsafe
        {
            return SDL_SyncWindow(SdlWindow);
        }
    }

    public override ShortSize RenderSizeInPixels => ReadRenderSizeInPixels(SdlWindow);

    public override Size<uint> Size
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

    public override bool MouseGrab
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

    public override bool WindowRelativeMouseMode
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

    public override bool AlwaysOnTop
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

    public override bool IsVisible
    {
        get
        {
            unsafe
            {
                return (SDL_GetWindowFlags(SdlWindow) & SDL_WindowFlags.SDL_WINDOW_HIDDEN) == 0;
            }
        }
    }

    public override bool IsRenderable
    {
        get
        {
            unsafe
            {
                return (SDL_GetWindowFlags(SdlWindow) & (SDL_WindowFlags.SDL_WINDOW_HIDDEN | SDL_WindowFlags.SDL_WINDOW_MINIMIZED)) == 0;
            }
        }
    }

    public override bool Show()
    {
        unsafe
        {
            return SDL_ShowWindow(SdlWindow);
        }
    }

    public override bool Hide()
    {
        unsafe
        {
            return SDL_HideWindow(SdlWindow);
        }
    }

    public override bool Raise()
    {
        unsafe
        {
            return SDL_RaiseWindow(SdlWindow);
        }
    }

    public override bool Minimize()
    {
        unsafe
        {
            return SDL_MinimizeWindow(SdlWindow);
        }
    }

    private bool _draggable;
    private GCHandle _hitTestHandle;
    private Func<Vector2Int, HitTestResult>? _hitTestCallback;

    public override bool Draggable
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

    public override void SetHitTest(Func<Vector2Int, HitTestResult>? callback)
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

    internal override void SetShapeCore(BitMask? shape)
    {
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

            int width = checked((int)shape.Size.Width);
            int height = checked((int)shape.Size.Height);
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
        NativeWindow self = (NativeWindow)handle.Target!;
        return self._hitTestCallback!(new Vector2Int(area->x, area->y)) == HitTestResult.Draggable
            ? SDL_HitTestResult.SDL_HITTEST_DRAGGABLE
            : SDL_HitTestResult.SDL_HITTEST_NORMAL;
    }

    public override Vector2Int Position
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

    public override void SetFullscreenBorderless(bool fullscreen)
    {
        unsafe
        {
            SDL_SetWindowFullscreen(SdlWindow, fullscreen);
        }
    }

    public override bool SetIcon(Image icon)
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

    internal override unsafe void ShowModalMessageBoxCore(MessageBoxSeverity severity, string title, string message)
    {
        MessageBox.Show(severity, title, message, SdlWindow);
    }

    internal override FileDialogResult ShowModalOpenFileDialogCore(IReadOnlyList<FileDialogFilter> filters, string? defaultLocation, bool allowMany)
    {
        return ShowModalFileDialog(filters, defaultLocation, allowMany, FileDialogKind.Open);
    }

    internal override FileDialogResult ShowModalSaveFileDialogCore(IReadOnlyList<FileDialogFilter> filters, string? defaultLocation)
    {
        return ShowModalFileDialog(filters, defaultLocation, false, FileDialogKind.Save);
    }

    internal override bool IsTextInputActive
    {
        get
        {
            unsafe
            {
                return SDL_TextInputActive(SdlWindow);
            }
        }
    }

    internal override void StartTextInput()
    {
        unsafe
        {
            SDL_StartTextInput(SdlWindow);
        }
    }

    internal override void StopTextInput()
    {
        unsafe
        {
            SDL_StopTextInput(SdlWindow);
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

    public override void Dispose()
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
