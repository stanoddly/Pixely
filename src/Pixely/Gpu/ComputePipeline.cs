using Pixely.ShaderCommon;
using Pixely.Utilities;
using XdlSharp;
using static XdlSharp.Xdl;

namespace Pixely.Gpu;

public class ComputePipeline : IDisposable
{
    private readonly GpuDevice _gpuDevice;
    internal Pointer<SDL_GPUComputePipeline> Pointer { get; set; }
    public ShaderBindingLayout BindingLayout { get; }

    public uint ThreadCountX { get; }
    public uint ThreadCountY { get; }
    public uint ThreadCountZ { get; }

    internal ComputePipeline(GpuDevice gpuDevice, Pointer<SDL_GPUComputePipeline> pointer, ShaderBindingLayout bindingLayout, uint threadCountX, uint threadCountY, uint threadCountZ)
    {
        _gpuDevice = gpuDevice;
        Pointer = pointer;
        BindingLayout = bindingLayout;
        ThreadCountX = threadCountX;
        ThreadCountY = threadCountY;
        ThreadCountZ = threadCountZ;
    }

    public void Dispose()
    {
        _gpuDevice.ReleaseComputePipeline(this);
    }
}
