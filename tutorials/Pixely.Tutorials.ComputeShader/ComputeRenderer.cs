using Pixely.Gpu;
using Pixely.RenderOrchestration;
using Pixely.Shaders;

namespace Pixely.Tutorials.ComputeShader;

public class ComputeRenderer : IRenderer<BasicRenderContext>
{
    private readonly ComputePipeline _computePipeline;
    private readonly Texture _outputTexture;
    private float _time;

    public ComputeRenderer(ComputePipeline computePipeline, Texture outputTexture)
    {
        _computePipeline = computePipeline;
        _outputTexture = outputTexture;
    }

    public void Render(BasicRenderContext renderContext)
    {
        _time += 0.016f;

        renderContext.CommandBuffer.PushComputeUniformData(0, _time);

        StorageTextureReadWriteBinding textureBinding = new StorageTextureReadWriteBinding
        {
            Texture = _outputTexture
        };

        ReadOnlySpan<StorageTextureReadWriteBinding> textureBindings = [textureBinding];

        using (IComputePass computePass = renderContext.CommandBuffer.CreateComputePass(
            textureBindings,
            ReadOnlySpan<StorageBufferReadWriteBinding>.Empty))
        {
            computePass.BindComputePipeline(_computePipeline);
            computePass.Dispatch(
                (uint)_outputTexture.Size.Width / _computePipeline.ThreadCountX,
                (uint)_outputTexture.Size.Height / _computePipeline.ThreadCountY,
                1);
        }

        renderContext.CommandBuffer.BlitTextures(_outputTexture, renderContext.SwapchainTexture);
    }

    public static ComputeRenderer Create(
        IComputeShaderLoader computeShaderLoader,
        ComputePipelineBuilder computePipelineBuilder,
        GpuDevice gpuDevice)
    {
        Pixely.Gpu.ComputeShader computeShader = computeShaderLoader.Load("shaders/compute");
        ComputePipeline computePipeline = computePipelineBuilder.Build(computeShader);

        Texture outputTexture = gpuDevice.CreateTexture(
            new ShortSize(512, 512),
            TextureFormat.R8G8B8A8Unorm,
            TextureUsage.ComputeStorageWrite | TextureUsage.Sampler);

        return new ComputeRenderer(computePipeline, outputTexture);
    }
}
