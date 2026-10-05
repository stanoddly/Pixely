using Pixely.Gpu;

namespace Pixely.App;

// The points of a frame that diagnostics measure. The frame loop, the render coordinator and the headless input console call
// it every frame; while diagnostics are off it is NullFrameTimingRecorder.
internal interface IFrameTimingRecorder
{
    void BeginFrame();

    // Stage transitions, events and updates end here.
    void EndUpdate();

    void EndRender();

    void EndFrame();

    void BeginSwapchainWait();

    void EndSwapchainWait();

    void OnSwapchainAcquired(Window window, SwapchainTexture swapchainTexture);

    // A headless app blocking on standard input for its next command, which is the operator's time, not the app's.
    void BeginInputWait();

    void EndInputWait();
}

internal sealed class NullFrameTimingRecorder : IFrameTimingRecorder
{
    public static NullFrameTimingRecorder Instance { get; } = new();

    private NullFrameTimingRecorder()
    {
    }

    public void BeginFrame()
    {
    }

    public void EndUpdate()
    {
    }

    public void EndRender()
    {
    }

    public void EndFrame()
    {
    }

    public void BeginSwapchainWait()
    {
    }

    public void EndSwapchainWait()
    {
    }

    public void OnSwapchainAcquired(Window window, SwapchainTexture swapchainTexture)
    {
    }

    public void BeginInputWait()
    {
    }

    public void EndInputWait()
    {
    }
}
