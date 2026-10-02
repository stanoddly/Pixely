using Pixely.Gpu;

namespace Pixely.RenderOrchestration;

// Runs the frame for an app that registers the GPU device but, on this frame, no render coordinator: an app without window
// rendering, or one whose rendering stage was unloaded. PixelyApp runs it only while no other coordinator is registered, since
// stages add and remove coordinators at runtime and two coordinators must not present the same window in one frame.
//
// Without it nothing would submit the uploads GpuMemorySystem records, so they would never run and every update of a buffer
// would cycle it into a new full-size copy. And every window is claimed for the device when it is created, but nothing would
// request its swapchain texture: SDL's Vulkan backend frees finished GPU work only on a submit whose command buffer requested
// one, or on a fence wait, so it would keep every finished submission. It therefore acquires a frame for each window claimed
// for the root's device, clears it and submits it after the uploads, the same way a RenderCoordinator does. A visible window
// presents black, and a frame paced by the display keeps the loop from spinning.
//
// A window claimed for another device, one a stage registered its own GPU device for, is left alone, and so is that device's
// GpuMemorySystem: a stage with its own device and no window rendering must submit its uploads itself. Offscreen windows are
// not claimed, so with only those it submits the uploads alone.
internal sealed class FallbackRenderCoordinator : IRenderCoordinator, IFrameDrawer
{
    private static readonly ColorTargetSettings BlackClear = new() { ClearColorValue = FColors.Black };

    private readonly GpuDevice _gpuDevice;
    private readonly IRenderCoordinatorGpu _gpu;
    private readonly WindowRegistry _windowRegistry;

    internal FallbackRenderCoordinator(GpuDevice gpuDevice, IRenderCoordinatorGpu gpu, WindowRegistry windowRegistry)
    {
        _gpuDevice = gpuDevice;
        _gpu = gpu;
        _windowRegistry = windowRegistry;
    }

    // True when a window showed the frame, or when the device has no window, so nothing paces the frame and the frame loop
    // does not wait, as for an app without windows.
    public bool Execute()
    {
        bool hasWindow = false;
        bool drawn = false;
        foreach ((_, _, Window window) in _windowRegistry.Windows)
        {
            if (window is SwapchainWindow swapchainWindow && swapchainWindow.SdlGpuDevice == _gpuDevice.SdlGpuDevice)
            {
                hasWindow = true;
                drawn |= RenderCoordinator.Execute(window, _gpu, this);
            }
        }

        if (!hasWindow)
        {
            _gpu.SubmitUploads();
            return true;
        }

        return drawn;
    }

    void IFrameDrawer.Draw(CommandBuffer commandBuffer, SwapchainTexture swapchainTexture)
    {
        using RenderPass renderPass = new RenderPassBuilder(commandBuffer)
            .AddColorTarget(swapchainTexture)
            .SetSharedColorTargetSettings(BlackClear)
            .Build();
    }
}
