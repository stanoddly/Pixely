using System.Diagnostics;
using Pixely.Content;
using Pixely.Gpu;
using Pixely.RenderOrchestration;
using Pixely.Utilities;
using SDL;

namespace Pixely;

/// <summary>
/// A hidden SDL window whose frames go to a texture instead of the swapchain, so nothing is presented and
/// frames can be read back. Everything else, events, size, text input and the colour target format, still
/// comes from the SDL window. Without a swapchain there is no vsync, so acquiring paces frames at a fixed interval.
/// </summary>
public sealed class OffscreenWindow : Window, IFrameCapture
{
    private readonly GpuDevice _gpuDevice;
    private readonly TimeSpan _frameInterval;
    // Requests made during a frame wait in _requested until that frame has been drawn, then move to _capturing
    // and are served from the texture at the next acquire, when it holds that frame.
    private List<Action<Image>> _requested = new();
    private List<Action<Image>> _capturing = new();
    private Texture? _colorTarget;
    private long _nextFrameTimestamp;

    internal OffscreenWindow(
        ViewScope viewScope,
        Pointer<SDL_Window> sdlWindow,
        GpuDevice gpuDevice,
        uint sdlId,
        PixelyFrameContext frameContext,
        PlatformInfo platformInfo,
        WindowCloseBehavior closeBehavior,
        TimeSpan frameInterval)
        : base(viewScope, sdlWindow, gpuDevice.SdlGpuDevice, sdlId, frameContext, platformInfo, closeBehavior)
    {
        if (frameInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(frameInterval), frameInterval, "The frame interval must be positive.");
        }

        _gpuDevice = gpuDevice;
        _frameInterval = frameInterval;
    }

    // There is always a frame to draw, whether or not the SDL window is shown.
    public override bool IsRenderable => true;

    // The whole point is that nothing reaches the desktop, so showing is refused rather than passed to SDL.
    public override bool Show() => false;

    public override bool TryWaitAndAcquireSwapchainTexture(CommandBuffer commandBuffer, out SwapchainTexture swapchainTexture)
    {
        ArgumentNullException.ThrowIfNull(commandBuffer);
        WaitForNextFrame();
        ServeCaptures();

        // The swapchain format keeps every pipeline built against ColorTargetFormat valid.
        Texture colorTarget = GetColorTarget(RenderSizeInPixels, ColorTargetFormat);

        // Renderers address the frame's target as SwapchainTexture, so the texture is aliased under that type without owning it.
        swapchainTexture = new SwapchainTexture(colorTarget.SdlGpuTexture, colorTarget.Size, colorTarget.Format);
        return true;
    }

    public void CaptureNextFrame(Action<Image> onCaptured)
    {
        ArgumentNullException.ThrowIfNull(onCaptured);
        _requested.Add(onCaptured);
    }

    public override void Dispose()
    {
        _colorTarget?.Dispose();
        _colorTarget = null;
        base.Dispose();
    }

    // Runs before the target is touched for the new frame, so a resize cannot replace the texture the captures wait on.
    private void ServeCaptures()
    {
        if (_capturing.Count > 0 && _colorTarget is { } lastFrame)
        {
            Image image = _gpuDevice.AcquireCommandBuffer().SubmitAndDownloadTexture(lastFrame);
            foreach (Action<Image> onCaptured in _capturing)
            {
                onCaptured(image);
            }
            _capturing.Clear();
        }

        (_requested, _capturing) = (_capturing, _requested);
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
        _nextFrameTimestamp = Math.Max(_nextFrameTimestamp, now) + (long)(_frameInterval.TotalSeconds * Stopwatch.Frequency);
    }
}
