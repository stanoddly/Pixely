using Pixely.Content;
using Pixely.Gpu;

namespace Pixely.Testing;

/// <summary>
/// The window of an app on a <see cref="StubGpuDevice"/>: a window without a platform, on the stub display of 1024 x 768 at
/// scale 1. Acquiring hands out a swapchain texture of the window's size at once, with no wait.
/// <list type="bullet">
/// <item>It opens at its <see cref="WindowConfig"/>'s size, 640 x 480 without one, or the display's when fullscreen, and at its
/// position, centred without one.</item>
/// <item>A new size reads back at once and raises <see cref="Window.ResolutionChanged"/> in the next frame, before any update.
/// Fullscreen covers the display and leaving it restores the window's size and position.</item>
/// <item>It is renderable while visible. <see cref="Window.Minimize"/> returns false and does nothing.</item>
/// <item>Always-on-top, mouse grab, relative mouse mode, dragging and text input keep what was set. The hit test and a shape are
/// accepted, and a shape needs a window created transparent.</item>
/// <item>A message box throws and a file dialog fails, because there is no platform to show them.</item>
/// </list>
/// </summary>
public sealed class StubWindow : Window
{
    private readonly StubGpuDevice _gpuDevice;
    private readonly StubEventSource _eventSource;
    private readonly bool _transparent;
    private Size<uint> _size;
    private Vector2Int _position;
    private Size<uint> _windowedSize;
    private Vector2Int _windowedPosition;
    private bool _fullscreen;
    private bool _visible;
    private bool _textInputActive;
    private bool _draggable;

    internal StubWindow(StubGpuDevice gpuDevice, StubEventSource eventSource, ViewScope viewScope, PixelyFrameClock frameClock, PlatformInfo platformInfo, WindowConfig config)
        : base(viewScope, frameClock, platformInfo, config.CloseBehavior, ToRenderSize(GetInitialSize(config)), 1, 1)
    {
        _gpuDevice = gpuDevice;
        _eventSource = eventSource;
        _transparent = config.Transparent;
        _windowedSize = config.Size ?? PixelyFactory.DefaultSize;
        _windowedPosition = GetPosition(config.Position, _windowedSize);
        _fullscreen = config.Fullscreen;
        _size = GetInitialSize(config);
        _position = _fullscreen ? new Vector2Int(StubDisplayService.Display.Bounds.X, StubDisplayService.Display.Bounds.Y) : _windowedPosition;
        _visible = config.InitiallyVisible;
        AlwaysOnTop = config.AlwaysOnTop;
    }

    public override float PixelDensity => 1;

    public override float DisplayScale => 1;

    public override Display Display => StubDisplayService.Display;

    public override bool Sync() => true;

    public override ShortSize RenderSizeInPixels => ToRenderSize(_size);

    public override Size<uint> Size
    {
        get => _size;
        set
        {
            _size = value;
            if (!_fullscreen)
            {
                _windowedSize = value;
            }

            _eventSource.QueueResize(this);
        }
    }

    // The format of a desktop window's sRGB swapchain on Vulkan and Direct3D 12.
    public override TextureFormat ColorTargetFormat => TextureFormat.B8G8R8A8UnormSrgb;

    public override bool MouseGrab { get; set; }

    public override bool WindowRelativeMouseMode { get; set; }

    public override bool AlwaysOnTop { get; set; }

    public override bool IsVisible => _visible;

    public override bool IsRenderable => _visible;

    public override bool Show()
    {
        _visible = true;
        return true;
    }

    public override bool Hide()
    {
        _visible = false;
        return true;
    }

    public override bool Raise() => true;

    public override bool Minimize() => false;

    public override bool Draggable
    {
        get => _draggable;
        set => _draggable = value;
    }

    // Nothing drags a window without a platform, so the callback is never called.
    public override void SetHitTest(Func<Vector2Int, HitTestResult>? callback)
    {
        _draggable = false;
    }

    internal override void SetShapeCore(BitMask? shape)
    {
        if (!_transparent)
        {
            throw new PixelyException("A window takes a shape only when it is created with WindowConfig.Transparent.");
        }
    }

    public override Vector2Int Position
    {
        get => _position;
        set
        {
            _position = value;
            if (!_fullscreen)
            {
                _windowedPosition = value;
            }
        }
    }

    public override bool TryWaitAndAcquireSwapchainTexture(CommandBuffer commandBuffer, out SwapchainTexture swapchainTexture)
    {
        ArgumentNullException.ThrowIfNull(commandBuffer);
        swapchainTexture = _gpuDevice.CreateSwapchainTexture(RenderSizeInPixels, ColorTargetFormat);
        return true;
    }

    public override void SetFullscreenBorderless(bool fullscreen)
    {
        if (fullscreen == _fullscreen)
        {
            return;
        }

        _fullscreen = fullscreen;
        Rectangle bounds = Display.Bounds;
        _size = fullscreen ? new Size<uint>((uint)bounds.Width, (uint)bounds.Height) : _windowedSize;
        _position = fullscreen ? new Vector2Int(bounds.X, bounds.Y) : _windowedPosition;
        _eventSource.QueueResize(this);
    }

    public override bool SetIcon(Image icon)
    {
        ArgumentNullException.ThrowIfNull(icon);
        return true;
    }

    internal override void ShowModalMessageBoxCore(MessageBoxSeverity severity, string title, string message)
    {
        throw new PixelyException("A test app has no message boxes.");
    }

    internal override FileDialogResult ShowModalOpenFileDialogCore(IReadOnlyList<FileDialogFilter> filters, string? defaultLocation, bool allowMany)
    {
        return FileDialogResult.Failed("A test app has no file dialogs.");
    }

    internal override FileDialogResult ShowModalSaveFileDialogCore(IReadOnlyList<FileDialogFilter> filters, string? defaultLocation)
    {
        return FileDialogResult.Failed("A test app has no file dialogs.");
    }

    internal override bool IsTextInputActive => _textInputActive;

    internal override void StartTextInput()
    {
        _textInputActive = true;
    }

    internal override void StopTextInput()
    {
        _textInputActive = false;
    }

    public override void Dispose()
    {
    }

    private static Size<uint> GetInitialSize(WindowConfig config)
    {
        Rectangle bounds = StubDisplayService.Display.Bounds;
        return config.Fullscreen ? new Size<uint>((uint)bounds.Width, (uint)bounds.Height) : config.Size ?? PixelyFactory.DefaultSize;
    }

    // A centred position, or none, centres the window on the stub display, as SDL's dummy driver does.
    private static Vector2Int GetPosition(WindowPosition? position, Size<uint> size)
    {
        if (position is { IsCentered: false } windowPosition)
        {
            return new Vector2Int(windowPosition.SdlX, windowPosition.SdlY);
        }

        Rectangle bounds = StubDisplayService.Display.Bounds;
        return new Vector2Int(bounds.X + (bounds.Width - (int)size.Width) / 2, bounds.Y + (bounds.Height - (int)size.Height) / 2);
    }

    private static ShortSize ToRenderSize(Size<uint> size) => new((ushort)size.Width, (ushort)size.Height);
}
