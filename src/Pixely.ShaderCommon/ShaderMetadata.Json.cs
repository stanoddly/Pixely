using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pixely.ShaderCommon;

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(ShaderMetadataHeaderDto))]
[JsonSerializable(typeof(GraphicsShaderProgramMetadataDto))]
[JsonSerializable(typeof(GraphicsShaderStageMetadataDto))]
[JsonSerializable(typeof(ComputeShaderMetadataDto))]
[JsonSerializable(typeof(ShaderStageDto))]
[JsonSerializable(typeof(ShaderKindDto))]
[JsonSerializable(typeof(ShaderFormatDto))]
[JsonSerializable(typeof(ShaderInstanceDto))]
[JsonSerializable(typeof(StorageBufferElementSizes))]
public partial class ShaderMetadataJsonContext: JsonSerializerContext;
