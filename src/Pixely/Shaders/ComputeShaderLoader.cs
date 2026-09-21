using Pixely.Content;
using Pixely.Gpu;
using Pixely.ShaderCommon;

namespace Pixely.Shaders;

public class ComputeShaderLoader : IComputeShaderLoader
{
    private const string GeneratedShaderDirectory = ".generated";
    private readonly ShaderFormats _shaderFormats;
    private readonly ComputeShaderMetadataLoader _shaderMetadataLoader;
    private readonly ContentSource _contentSource;

    internal ComputeShaderLoader(GpuDevice gpuDevice, ComputeShaderMetadataLoader shaderMetadataLoader, ContentSource contentSource)
    {
        _shaderFormats = gpuDevice.GetSupportedShaderFormats();
        _shaderMetadataLoader = shaderMetadataLoader;
        _contentSource = contentSource;
    }

    public ComputeShader Load(ReadOnlySpan<char> path)
    {
        string pathString = path.ToString();
        string name = VirtualPath.GetFileName(pathString);
        string? directoryName = VirtualPath.GetDirectoryName(pathString);

        string generatedDirectoryName;
        if (directoryName == null)
        {
            generatedDirectoryName = GeneratedShaderDirectory;
        }
        else
        {
            generatedDirectoryName = VirtualPath.Combine(directoryName, GeneratedShaderDirectory);
        }

        string metadataFilename = VirtualPath.Combine(generatedDirectoryName, $"{name}.metadata.json");
        ComputeShaderMetadata shaderMetadata = _shaderMetadataLoader.Load(metadataFilename);

        foreach (ShaderInstance shaderInstance in shaderMetadata.Shaders)
        {
            if (_shaderFormats.Contains(shaderInstance.Format))
            {
                return CreateComputeShader(generatedDirectoryName, shaderInstance, shaderMetadata);
            }
        }

        throw new NotSupportedException("No compatible shader format found for this GPU.");
    }

    private ComputeShader CreateComputeShader(string directory, ShaderInstance shaderInstance, ComputeShaderMetadata shaderMetadata)
    {
        string filePath = VirtualPath.Combine(directory, shaderInstance.Filename);
        ContentFile file = _contentSource.GetFile(filePath);
        using Stream stream = file.Open();

        int codeSize = (int)stream.Length;
        // SDL reads a text shader as a C string as well as by size, so the buffer needs a terminator that
        // the size does not count.
        bool isTextFormat = ShaderFormats.TextFormats.Contains(shaderInstance.Format);
        byte[] code = new byte[isTextFormat ? codeSize + 1 : codeSize];
        stream.ReadExactly(code.AsSpan(0, codeSize));

        return new ComputeShader(
            code,
            codeSize,
            shaderInstance.EntryPoint,
            shaderInstance.Format,
            shaderMetadata.BindingLayout,
            shaderMetadata.ThreadCountX,
            shaderMetadata.ThreadCountY,
            shaderMetadata.ThreadCountZ);
    }
}
