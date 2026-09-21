using System.Runtime.Serialization;

namespace Pixely.ShaderCommon;

public enum ShaderStageDto
{
    [EnumMember(Value = "vertex")]
    Vertex = 0,

    [EnumMember(Value = "fragment")]
    Fragment = 1,

    [EnumMember(Value = "compute")]
    Compute = 2
}

public enum ShaderKindDto
{
    Graphics = 0
}

public enum ShaderFormatDto
{
    [EnumMember(Value = "private")]
    Private = 0,

    [EnumMember(Value = "spirv")]
    SpirV = 1,

    [EnumMember(Value = "dxbc")]
    Dxbc = 2,

    [EnumMember(Value = "dxil")]
    Dxil = 3,

    [EnumMember(Value = "msl")]
    Msl = 4,

    [EnumMember(Value = "metallib")]
    MetalLib = 5,

    [EnumMember(Value = "wgsl")]
    Wgsl = 6
}

public record ShaderInstanceDto(ShaderFormatDto Format, string Filename, string EntryPoint);

public record ShaderMetadataHeaderDto
{
    public ShaderStageDto? Stage { get; init; }
    public ShaderKindDto? Kind { get; init; }
    public required string SourceHash { get; init; }
    public List<string>? SourceDependencies { get; init; }
    public string? SlangVersion { get; init; }
}

public record GraphicsShaderStageMetadataDto
{
    public required ShaderBindingLayout BindingLayout { get; init; }
    public required List<ShaderInstanceDto> Shaders { get; init; }
}

public record GraphicsVertexShaderStageMetadataDto : GraphicsShaderStageMetadataDto
{
    public ShaderSystemValueInputs SystemValueInputs { get; init; }
}

public record GraphicsShaderProgramMetadataDto
{
    public ShaderKindDto Kind { get; init; } = ShaderKindDto.Graphics;
    public required GraphicsVertexShaderStageMetadataDto Vertex { get; init; }
    public required GraphicsShaderStageMetadataDto Fragment { get; init; }
    public required string SourceHash { get; init; }
    public List<string>? SourceDependencies { get; init; }
    public string? SlangVersion { get; init; }
}

public readonly record struct ShaderSystemValueInputs(bool UsesVertexId, bool UsesInstanceId);

public record ComputeShaderMetadataDto
{
    public ShaderStageDto Stage { get; init; } = ShaderStageDto.Compute;
    public required ShaderBindingLayout BindingLayout { get; init; }
    public required List<ShaderInstanceDto> Shaders { get; init; }
    public required string SourceHash { get; init; }
    public List<string>? SourceDependencies { get; init; }
    public string? SlangVersion { get; init; }
    public required uint ThreadCountX { get; init; }
    public required uint ThreadCountY { get; init; }
    public required uint ThreadCountZ { get; init; }
}
