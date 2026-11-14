using Pixely.Content;
using Pixely.Gpu;
using Pixely.Headless;

namespace Pixely.Tests;

// A window without a platform. It starts at the size, density and scale it is created with, and a test sets what its getters
// return afterwards. Members no test needs throw.
internal sealed class FixedWindow : Window
{
    internal FixedWindow(ViewScope viewScope, ShortSize renderSize = default, float pixelDensity = 1, float displayScale = 1)
        : base(viewScope, new FixedStepFrameClock(), new PlatformInfo((string?)null), WindowCloseBehavior.QuitApplication, renderSize, pixelDensity, displayScale)
    {
        RenderSize = renderSize;
        Density = pixelDensity;
        Scale = displayScale;
    }

    public override ShortSize RenderSizeInPixels => RenderSize;

    public override float PixelDensity => Density;

    public override float DisplayScale => Scale;

    internal ShortSize RenderSize { get; set; }

    internal float Density { get; set; }

    internal float Scale { get; set; }

    public override Display Display => throw new NotSupportedException();

    public override Size<uint> Size
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override TextureFormat ColorTargetFormat => throw new NotSupportedException();

    public override bool MouseGrab { get; set; }

    public override bool WindowRelativeMouseMode { get; set; }

    public override bool AlwaysOnTop { get; set; }

    public override bool IsVisible => true;

    public override bool IsRenderable => true;

    public override bool Draggable { get; set; }

    public override Vector2Int Position { get; set; }

    internal override bool IsTextInputActive => false;

    public override bool Sync() => true;

    public override bool Show() => true;

    public override bool Hide() => true;

    public override bool Raise() => true;

    public override bool Minimize() => false;

    public override void SetHitTest(Func<Vector2Int, HitTestResult>? callback)
    {
    }

    internal override void SetShapeCore(BitMask? shape)
    {
    }

    public override bool TryWaitAndAcquireSwapchainTexture(CommandBuffer commandBuffer, out SwapchainTexture swapchainTexture) => throw new NotSupportedException();

    public override void SetFullscreenBorderless(bool fullscreen) => throw new NotSupportedException();

    public override bool SetIcon(Image icon) => true;

    internal override void ShowModalMessageBoxCore(MessageBoxSeverity severity, string title, string message) => throw new NotSupportedException();

    internal override FileDialogResult ShowModalOpenFileDialogCore(IReadOnlyList<FileDialogFilter> filters, string? defaultLocation, bool allowMany) => throw new NotSupportedException();

    internal override FileDialogResult ShowModalSaveFileDialogCore(IReadOnlyList<FileDialogFilter> filters, string? defaultLocation) => throw new NotSupportedException();

    internal override void StartTextInput() => throw new NotSupportedException();

    internal override void StopTextInput() => throw new NotSupportedException();

    public override void Dispose()
    {
    }
}
