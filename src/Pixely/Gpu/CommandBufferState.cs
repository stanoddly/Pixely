using Pixely.ShaderCommon;
using Pixely.Utilities;
using SDL;

namespace Pixely.Gpu;

internal enum OpenPassKind : byte
{
    None,
    Render,
    Compute
}

/// <summary>
/// Everything a <see cref="CommandBuffer"/> records against, the open pass included. It is a plain struct so that the passes can
/// hold a <c>ref</c> to it: a <c>ref</c> field cannot point to a <c>ref struct</c> such as <see cref="CommandBuffer"/> itself.
/// SDL allows one open pass per command buffer, which is why the pass state lives here rather than in the passes.
/// </summary>
internal struct CommandBufferState
{
    public GpuDevice GpuDevice;
    public Pointer<SDL_GPUCommandBuffer> SdlGpuCommandBuffer;
    public ShaderUniformSlotSizes FragmentShaderUniformSlotSizes;
    public ShaderUniformSlotSizes VertexShaderUniformSlotSizes;
    public OpenPassKind OpenPass;

    // Numbers every pass begun on this command buffer, so a pass handle kept after its pass ended is detected.
    public uint PassNumber;

    public RenderPassState RenderPass;
    public ComputePassState ComputePass;

    public readonly bool IsPassOpen(OpenPassKind kind, uint passNumber)
    {
        return OpenPass == kind && PassNumber == passNumber;
    }

    public readonly void ThrowIfDisposed()
    {
        if (SdlGpuCommandBuffer.IsNull)
        {
            throw new ObjectDisposedException(nameof(CommandBuffer));
        }
    }

    public readonly void ThrowIfPassOpen()
    {
        if (OpenPass != OpenPassKind.None)
        {
            throw new InvalidOperationException($"A {OpenPass.ToString().ToLowerInvariant()} pass is still open on this command buffer. Dispose it first.");
        }
    }

    public uint OpenNextPass(OpenPassKind kind)
    {
        PassNumber++;
        OpenPass = kind;
        return PassNumber;
    }

    public void ClosePass()
    {
        OpenPass = OpenPassKind.None;
        RenderPass = default;
        ComputePass = default;
    }
}

internal struct RenderPassState
{
    public Pointer<SDL_GPURenderPass> NativePointer;
    public uint VerticesCount;
    public GpuIndexBuffer? IndexBuffer;
    public RenderPassValidator Validator;
    public ShaderBindingCounts FragmentShaderBindingCounts;
    public ShaderBindingCounts VertexShaderBindingCounts;
    public DepthBufferFormat DepthBufferFormat;
    public ShortSize TargetSize;
}

internal struct ComputePassState
{
    public Pointer<SDL_GPUComputePass> NativePointer;
    public uint ReadWriteStorageTextureCount;
    public uint ReadWriteStorageBufferCount;
    public ComputePipeline? BoundPipeline;
    public StorageBufferElementSizes ReadOnlyStorageBufferElementSizes;
    public StorageBufferElementSizes ReadWriteStorageBufferElementSizes;
}
