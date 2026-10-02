using Pixely.Gpu;

namespace Pixely.RenderOrchestration;

// The GPU calls RenderCoordinator makes for a frame. Separate so tests can run the coordinator without a GPU device.
internal interface IRenderCoordinatorGpu
{
    CommandBuffer AcquireCommandBuffer();
    void SubmitUploads();
    void Submit(CommandBuffer commandBuffer);
    void Cancel(CommandBuffer commandBuffer);
}

internal sealed class RenderCoordinatorGpu : IRenderCoordinatorGpu
{
    private readonly GpuDevice _gpuDevice;
    private readonly GpuMemorySystem _gpuMemorySystem;

    internal RenderCoordinatorGpu(GpuDevice gpuDevice, GpuMemorySystem gpuMemorySystem)
    {
        _gpuDevice = gpuDevice;
        _gpuMemorySystem = gpuMemorySystem;
    }

    public CommandBuffer AcquireCommandBuffer()
    {
        return _gpuDevice.AcquireCommandBuffer();
    }

    public void SubmitUploads()
    {
        _gpuMemorySystem.Submit();
    }

    public void Submit(CommandBuffer commandBuffer)
    {
        commandBuffer.Submit();
    }

    public void Cancel(CommandBuffer commandBuffer)
    {
        commandBuffer.Cancel();
    }
}
