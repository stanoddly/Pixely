using Pixely.Content;
using Pixely.Gpu;
using Pixely.Utilities;
using SDL;

namespace Pixely.Headless;

/// <summary>
/// The window of a headless app: a hidden SDL window whose frames go to a texture
/// instead of the swapchain, so nothing is presented and frames can be read back. Events, size and text input still come from
/// the SDL window. Without a swapchain there is no vsync, so acquiring waits for the GPU to finish all earlier work instead, which
/// keeps at most about one frame in flight.
/// </summary>
public sealed class OffscreenWindow : Window
{
    private readonly GpuDevice _gpuDevice;
    private Texture? _colorTarget;

    internal OffscreenWindow(
        ViewScope viewScope,
        Pointer<SDL_Window> sdlWindow,
        GpuDevice gpuDevice,
        TextureFormat colorTargetFormat,
        uint sdlId,
        PixelyFrameClock frameClock,
        PlatformInfo platformInfo,
        WindowCloseBehavior closeBehavior)
        : base(viewScope, sdlWindow, sdlId, frameClock, platformInfo, closeBehavior)
    {
        _gpuDevice = gpuDevice;
        ColorTargetFormat = colorTargetFormat;
    }

    public override TextureFormat ColorTargetFormat { get; }

    /// <summary>
    /// The sRGB format a desktop window's SDR_LINEAR swapchain has: B8G8R8A8UnormSrgb on Vulkan and D3D12, R8G8B8A8UnormSrgb on a
    /// Vulkan driver without it. Throws when the device supports neither, the same as a desktop window without an sRGB swapchain.
    /// </summary>
    internal static TextureFormat SelectColorTargetFormat(Func<TextureFormat, bool> isSupported)
    {
        if (isSupported(TextureFormat.B8G8R8A8UnormSrgb))
        {
            return TextureFormat.B8G8R8A8UnormSrgb;
        }

        if (isSupported(TextureFormat.R8G8B8A8UnormSrgb))
        {
            return TextureFormat.R8G8B8A8UnormSrgb;
        }

        throw new PixelyInitializationException("The GPU device supports neither B8G8R8A8UnormSrgb nor R8G8B8A8UnormSrgb as a sampled color target, which an offscreen window needs.");
    }

    internal override bool IsOffscreen => true;

    // There is always a frame to draw, whether or not the SDL window is shown.
    public override bool IsRenderable => true;

    // The whole point is that nothing reaches the desktop, so showing is refused rather than passed to SDL.
    public override bool Show() => false;

    public override bool TryWaitAndAcquireSwapchainTexture(CommandBuffer commandBuffer, out SwapchainTexture swapchainTexture)
    {
        ArgumentNullException.ThrowIfNull(commandBuffer);
        WaitForSubmittedWork();

        Texture colorTarget = GetColorTarget(RenderSizeInPixels, ColorTargetFormat);

        // Renderers address the frame's target as SwapchainTexture, so the texture is aliased under that type without owning it.
        swapchainTexture = new SwapchainTexture(colorTarget.SdlGpuTexture, colorTarget.Size, colorTarget.Format, colorTarget.Usage);
        return true;
    }

    /// <summary>
    /// Reads back the last rendered frame. Waits for the GPU, so the frame this runs in takes longer.
    /// Frame thread only, like every other member of a window.
    /// </summary>
    public Image CaptureLastFrame()
    {
        if (_colorTarget is not { } lastFrame)
        {
            throw new InvalidOperationException("No frame has been rendered yet.");
        }

        return _gpuDevice.AcquireCommandBuffer().SubmitAndDownloadTexture(lastFrame);
    }

    public override void Dispose()
    {
        _colorTarget?.Dispose();
        _colorTarget = null;
        base.Dispose();
    }

    // The device has one queue, so a fence on an empty command buffer signals once everything submitted before it is done,
    // whoever submitted it.
    private void WaitForSubmittedWork()
    {
        using (GpuFence fence = _gpuDevice.AcquireCommandBuffer().SubmitAndAcquireFence())
        {
            _gpuDevice.WaitForFences([fence]);
        }
    }

    private Texture GetColorTarget(ShortSize size, TextureFormat format)
    {
        if (_colorTarget is { } current && current.Size == size && current.Format == format)
        {
            return current;
        }

        _colorTarget?.Dispose();
        _colorTarget = _gpuDevice.CreateColorTargetTexture(size, format);
        return _colorTarget;
    }
}
