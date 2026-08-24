using Pixely.Content;
using Pixely.Gpu;
using Pixely.ShaderCommon;
using SDL;

namespace Pixely.Shaders;

public class ShaderLoader : IShaderLoader
{
    private const string GeneratedShaderDirectory = ".generated";
    private readonly GpuDevice _gpuDevice;
    private readonly GraphicsShaderProgramMetadataLoader _shaderMetadataLoader;
    private readonly ShaderFormats _shaderFormats;
    private readonly ContentSource _contentSource;

    internal ShaderLoader(
        GpuDevice gpuDevice,
        GraphicsShaderProgramMetadataLoader shaderMetadataLoader,
        ContentSource contentSource)
    {
        _gpuDevice = gpuDevice;
        _shaderMetadataLoader = shaderMetadataLoader;
        _contentSource = contentSource;
        _shaderFormats = _gpuDevice.GetSupportedShaderFormats();
    }

    public GraphicsShaderProgram LoadGraphicsShaderProgram(ReadOnlySpan<char> path)
    {
        string pathString = path.ToString();
        string name = VirtualPath.GetFileName(pathString);
        string? directoryName = VirtualPath.GetDirectoryName(pathString);
        string generatedDirectoryName = directoryName == null
            ? GeneratedShaderDirectory
            : VirtualPath.Combine(directoryName, GeneratedShaderDirectory);
        string metadataFilename = VirtualPath.Combine(generatedDirectoryName, $"{name}.metadata.json");
        GraphicsShaderProgramMetadata metadata = _shaderMetadataLoader.Load(metadataFilename);

        GraphicsShader? vertexShader = null;
        GraphicsShader? fragmentShader = null;
        try
        {
            vertexShader = LoadStage(
                generatedDirectoryName,
                metadata.Vertex,
                SDL_GPUShaderStage.SDL_GPU_SHADERSTAGE_VERTEX);
            fragmentShader = LoadStage(
                generatedDirectoryName,
                metadata.Fragment,
                SDL_GPUShaderStage.SDL_GPU_SHADERSTAGE_FRAGMENT);

            GraphicsShaderProgram shaderProgram = new GraphicsShaderProgram(
                _gpuDevice,
                vertexShader,
                fragmentShader);
            _gpuDevice.RegisterGraphicsShaderProgram(shaderProgram);
            return shaderProgram;
        }
        catch
        {
            if (vertexShader != null)
            {
                _gpuDevice.ReleaseShader(vertexShader);
            }

            if (fragmentShader != null)
            {
                _gpuDevice.ReleaseShader(fragmentShader);
            }

            throw;
        }
    }

    private GraphicsShader LoadStage(
        string directory,
        GraphicsShaderStageMetadata metadata,
        SDL_GPUShaderStage stage)
    {
        foreach (ShaderInstance shaderInstance in metadata.Shaders)
        {
            if (_shaderFormats.Contains(shaderInstance.Format))
            {
                return CreateShader(
                    directory,
                    shaderInstance,
                    metadata.BindingLayout,
                    metadata.SystemValueInputs,
                    stage);
            }
        }

        string stageName = stage == SDL_GPUShaderStage.SDL_GPU_SHADERSTAGE_VERTEX ? "vertex" : "fragment";
        throw new NotSupportedException($"No compatible {stageName} shader format found for this GPU.");
    }

    private GraphicsShader CreateShader(
        string directory,
        ShaderInstance shaderInstance,
        ShaderBindingLayout bindingLayout,
        ShaderSystemValueInputs systemValueInputs,
        SDL_GPUShaderStage stage)
    {
        string path = VirtualPath.Combine(directory, shaderInstance.Filename);
        ContentFile file = _contentSource.GetFile(path);
        using Stream stream = file.Open();
        byte[] shaderCode = new byte[stream.Length];
        stream.ReadExactly(shaderCode);
        byte[] entryPoint = System.Text.Encoding.UTF8.GetBytes(shaderInstance.EntryPoint + "\0");

        unsafe
        {
            fixed (byte* shaderCodePointer = shaderCode)
            fixed (byte* entryPointPointer = entryPoint)
            {
                SDL_GPUShaderCreateInfo createInfo = new()
                {
                    code = shaderCodePointer,
                    code_size = (nuint)shaderCode.Length,
                    entrypoint = entryPointPointer,
                    format = (SDL_GPUShaderFormat)shaderInstance.Format,
                    stage = stage,
                    num_samplers = (uint)bindingLayout.NumSamplers(),
                    num_uniform_buffers = (uint)bindingLayout.NumUniformBuffers(),
                    num_storage_buffers = (uint)bindingLayout.NumStorageBuffers(),
                    num_storage_textures = (uint)bindingLayout.NumStorageTextures()
                };

                SDL_GPUShader* pointer = SDL3.SDL_CreateGPUShader(_gpuDevice.SdlGpuDevice, &createInfo);
                if (pointer == null)
                {
                    string stageName = stage == SDL_GPUShaderStage.SDL_GPU_SHADERSTAGE_VERTEX
                        ? "vertex"
                        : "fragment";
                    throw new PixelyInitializationException(
                        $"SDL_CreateGPUShader failed for {stageName} stage: " +
                        SDL3.SDL_GetError());
                }

                return new GraphicsShader(pointer, bindingLayout, systemValueInputs);
            }
        }
    }
}
