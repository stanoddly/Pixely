using System.Diagnostics;
using Pixely.Content;
using Pixely.Gpu;
using Pixely.Utilities;
using SDL;

namespace Pixely;

/// <summary>
/// A hidden SDL window whose frames go to a texture instead of the swapchain, so nothing is presented and
/// frames can be read back. Everything else, events, size, text input and the colour target format, still
/// comes from the SDL window. Without a swapchain there is no vsync, so acquiring paces frames at <see cref="FrameInterval"/>.
/// </summary>
public sealed class OffscreenWindow : Window
{
    // Nobody watches these frames, so a modest constant rate is enough and keeps the frame loop off a full core.
    public static readonly TimeSpan FrameInterval = TimeSpan.FromSeconds(1.0 / 30);

    private readonly GpuDevice _gpuDevice;
    private Texture? _colorTarget;
    private long _nextFrameTimestamp;

    internal OffscreenWindow(
        ViewScope viewScope,
        Pointer<SDL_Window> sdlWindow,
        GpuDevice gpuDevice,
        uint sdlId,
        PixelyFrameContext frameContext,
        PlatformInfo platformInfo,
        WindowCloseBehavior closeBehavior)
        : base(viewScope, sdlWindow, gpuDevice.SdlGpuDevice, sdlId, frameContext, platformInfo, closeBehavior)
    {
        _gpuDevice = gpuDevice;
    }

    // There is always a frame to draw, whether or not the SDL window is shown.
    public override bool IsRenderable => true;

    // The whole point is that nothing reaches the desktop, so showing is refused rather than passed to SDL.
    public override bool Show() => false;

    public override bool TryWaitAndAcquireSwapchainTexture(CommandBuffer commandBuffer, out SwapchainTexture swapchainTexture)
    {
        ArgumentNullException.ThrowIfNull(commandBuffer);
        WaitForNextFrame();

        // The swapchain format keeps every pipeline built against ColorTargetFormat valid.
        Texture colorTarget = GetColorTarget(RenderSizeInPixels, ColorTargetFormat);

        // Renderers address the frame's target as SwapchainTexture, so the texture is aliased under that type without owning it.
        swapchainTexture = new SwapchainTexture(colorTarget.SdlGpuTexture, colorTarget.Size, colorTarget.Format);
        return true;
    }

    /// <summary>Reads back the last rendered frame. Waits for the GPU, so the frame this runs in takes longer.</summary>
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

    private void WaitForNextFrame()
    {
        long now = Stopwatch.GetTimestamp();
        if (_nextFrameTimestamp == 0)
        {
            _nextFrameTimestamp = now;
        }

        TimeSpan remaining = Stopwatch.GetElapsedTime(now, _nextFrameTimestamp);
        if (remaining > TimeSpan.Zero)
        {
            Thread.Sleep(remaining);
            now = _nextFrameTimestamp;
        }

        // Never schedule into the past, otherwise a long frame would be followed by a burst of unpaced ones.
        _nextFrameTimestamp = Math.Max(_nextFrameTimestamp, now) + (long)(FrameInterval.TotalSeconds * Stopwatch.Frequency);
    }
}
