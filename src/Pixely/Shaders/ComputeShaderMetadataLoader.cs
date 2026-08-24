using System.Text.Json;
using Pixely.Content;
using Pixely.ShaderCommon;

namespace Pixely.Shaders;

public class ComputeShaderMetadataLoader
{
    private readonly ContentSource _contentSource;

    public ComputeShaderMetadataLoader(ContentSource contentSource)
    {
        _contentSource = contentSource;
    }

    public ComputeShaderMetadata Load(ReadOnlySpan<char> path)
    {
        using Stream stream = _contentSource.GetFile(path).Open();
        ComputeShaderMetadataDto? dto = JsonSerializer.Deserialize(stream, ShaderMetadataJsonContext.Default.ComputeShaderMetadataDto);

        if (dto == null)
        {
            throw new InvalidOperationException($"Failed to deserialize compute shader metadata from path: {path.ToString()}");
        }
        if (dto.Stage != ShaderStageDto.Compute)
        {
            throw new ArgumentException($"Expected compute shader but got {dto.Stage}");
        }

        return new ComputeShaderMetadata
        {
            BindingLayout = dto.BindingLayout,
            Shaders = GraphicsShaderProgramMetadataLoader.ConvertShaderInstances(dto.Shaders),
            ThreadCountX = dto.ThreadCountX,
            ThreadCountY = dto.ThreadCountY,
            ThreadCountZ = dto.ThreadCountZ
        };
    }
}
