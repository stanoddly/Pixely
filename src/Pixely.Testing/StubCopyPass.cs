using Pixely.Gpu;
using Pixely.Utilities;
using SDL;

namespace Pixely.Testing;

// Keeps each upload in its command buffer, which applies them to the device's contents once it is submitted.
internal sealed class StubCopyPass : CopyPass
{
    private readonly StubCommandBuffer _commandBuffer;

    internal StubCopyPass(StubGpuDevice gpuDevice, StubCommandBuffer commandBuffer) : base(gpuDevice)
    {
        _commandBuffer = commandBuffer;
    }

    internal override void UploadToBufferCore(ReadOnlySpan<byte> data, Pointer<SDL_GPUBuffer> buffer, bool cycle)
    {
        _commandBuffer.AddUpload(new StubUpload(false, StubGpuDevice.HandleOf(buffer), 0, data.ToArray()));
    }

    internal override void UploadToTextureCore(ReadOnlySpan<byte> data, Pointer<SDL_GPUTexture> texture, uint layer, uint width, uint height)
    {
        _commandBuffer.AddUpload(new StubUpload(true, StubGpuDevice.HandleOf(texture), layer, data.ToArray()));
    }

    internal override void EndCore()
    {
    }
}
