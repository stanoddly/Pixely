using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Pixely.Content;
using Pixely.Gpu;

namespace Pixely.RenderOrchestration;

/// <summary>
/// Renders every frame into a texture instead of the window's swapchain, so nothing is presented and frames can be read back.
/// Without a swapchain there is no vsync, so the provider paces frames itself at a fixed interval.
/// </summary>
public sealed class OffscreenRenderContextProvider : RenderContextProvider<BasicRenderContext>, IFrameCapture, IDisposable
{
    private readonly GpuDevice _gpuDevice;
    private readonly TimeSpan _frameInterval;
    private readonly List<Action<Image>> _captureRequests = new();
    private Texture? _colorTarget;
    private long _nextFrameTimestamp;

    public OffscreenRenderContextProvider(GpuDevice gpuDevice, TimeSpan frameInterval)
    {
        ArgumentNullException.ThrowIfNull(gpuDevice);
        if (frameInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(frameInterval), frameInterval, "The frame interval must be positive.");
        }

        _gpuDevice = gpuDevice;
        _frameInterval = frameInterval;
    }

    // The texture is there whether or not the window is shown.
    public override bool CanRender(Window window) => true;

    public override bool TryCreateRenderContext(Window window, [NotNullWhen(true)] out BasicRenderContext? renderContext)
    {
        ArgumentNullException.ThrowIfNull(window);
        WaitForNextFrame();

        // The swapchain format keeps every pipeline built against window.ColorTargetFormat valid.
        Texture colorTarget = GetColorTarget(window.RenderSizeInPixels, window.ColorTargetFormat);
        renderContext = new OffscreenRenderContext(this, colorTarget, _gpuDevice.AcquireCommandBuffer());
        return true;
    }

    public void CaptureNextFrame(Action<Image> onCaptured)
    {
        ArgumentNullException.ThrowIfNull(onCaptured);
        _captureRequests.Add(onCaptured);
    }

    public void Dispose()
    {
        _colorTarget?.Dispose();
        _colorTarget = null;
    }

    internal void CompleteFrame(CommandBuffer commandBuffer, Texture colorTarget)
    {
        if (_captureRequests.Count == 0)
        {
            commandBuffer.Submit();
            return;
        }

        Image image = commandBuffer.SubmitAndDownloadTexture(colorTarget);
        // A callback may request another capture, so hand out this frame's requests before running them.
        Action<Image>[] requests = _captureRequests.ToArray();
        _captureRequests.Clear();
        foreach (Action<Image> request in requests)
        {
            request(image);
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

    private sealed class OffscreenRenderContext : BasicRenderContext
    {
        private readonly OffscreenRenderContextProvider _provider;

        // Renderers address the frame's target as SwapchainTexture, so the offscreen texture is aliased under that type without owning it.
        internal OffscreenRenderContext(OffscreenRenderContextProvider provider, Texture colorTarget, CommandBuffer commandBuffer)
            : base(new SwapchainTexture(colorTarget.SdlGpuTexture, colorTarget.Size, colorTarget.Format), commandBuffer)
        {
            _provider = provider;
        }

        public override void Dispose()
        {
            _provider.CompleteFrame(CommandBuffer, ColorTarget);
        }
    }
}
