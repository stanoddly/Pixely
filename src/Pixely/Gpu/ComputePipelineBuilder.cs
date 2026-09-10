using SDL;

namespace Pixely.Gpu;

public class ComputePipelineBuilder
{
    private readonly GpuDevice _gpuDevice;

    internal ComputePipelineBuilder(GpuDevice gpuDevice)
    {
        _gpuDevice = gpuDevice;
    }

    public ComputePipeline Build(ComputeShader computeShader)
    {
        byte[] entryPoint = System.Text.Encoding.UTF8.GetBytes(computeShader.EntryPoint + "\0");
        ShaderCommon.ShaderBindingLayout bindingLayout = computeShader.BindingLayout;

        unsafe
        {
            fixed (byte* shaderCodePointer = computeShader.Code)
            fixed (byte* entryPointPointer = entryPoint)
            {
                SDL_GPUComputePipelineCreateInfo createInfo = new()
                {
                    code = shaderCodePointer,
                    code_size = (nuint)computeShader.CodeSize,
                    entrypoint = entryPointPointer,
                    format = (SDL_GPUShaderFormat)computeShader.Format,
                    num_samplers = (uint)bindingLayout.NumSamplers(),
                    num_readonly_storage_textures = (uint)bindingLayout.BindingCounts.NumStorageTextures,
                    num_readonly_storage_buffers = (uint)bindingLayout.BindingCounts.NumStorageBuffers,
                    num_readwrite_storage_textures = (uint)bindingLayout.BindingCounts.NumReadWriteStorageTextures,
                    num_readwrite_storage_buffers = (uint)bindingLayout.BindingCounts.NumReadWriteStorageBuffers,
                    num_uniform_buffers = (uint)bindingLayout.NumUniformBuffers(),
                    threadcount_x = computeShader.ThreadCountX,
                    threadcount_y = computeShader.ThreadCountY,
                    threadcount_z = computeShader.ThreadCountZ
                };

                SDL_GPUComputePipeline* pipeline = SDL3.SDL_CreateGPUComputePipeline(_gpuDevice.SdlGpuDevice, &createInfo);
                if (pipeline == null)
                {
                    throw new PixelyInitializationException($"SDL_CreateGPUComputePipeline failed: {SDL3.SDL_GetError()}");
                }

                ComputePipeline computePipeline = new ComputePipeline(_gpuDevice, pipeline, bindingLayout, computeShader.ThreadCountX, computeShader.ThreadCountY, computeShader.ThreadCountZ);
                _gpuDevice.RegisterComputePipeline(computePipeline);
                return computePipeline;
            }
        }
    }
}
