using System.Runtime.Serialization;
using Pixely.ShaderCommon;
using SDL;

namespace Pixely.Shaders;

public enum ShaderFormat: uint
{
    [EnumMember(Value = "private")]
    Private = SDL_GPUShaderFormat.SDL_GPU_SHADERFORMAT_PRIVATE,
    
    [EnumMember(Value = "spirv")]
    SpirV = SDL_GPUShaderFormat.SDL_GPU_SHADERFORMAT_SPIRV,
    
    [EnumMember(Value = "dxbc")]
    Dxbc = SDL_GPUShaderFormat.SDL_GPU_SHADERFORMAT_DXBC,
    
    [EnumMember(Value = "dxil")]
    Dxil = SDL_GPUShaderFormat.SDL_GPU_SHADERFORMAT_DXIL,
    
    [EnumMember(Value = "msl")]
    Msl = SDL_GPUShaderFormat.SDL_GPU_SHADERFORMAT_MSL,
    
    [EnumMember(Value = "metallib")]
    MetalLib = SDL_GPUShaderFormat.SDL_GPU_SHADERFORMAT_METALLIB,

    // SDL_GPU_SHADERFORMAT_WGSL, which ppy.SDL3-CS does not expose yet
    [EnumMember(Value = "wgsl")]
    Wgsl = 1u << 6
}

public readonly struct ShaderFormats
{
    public static readonly ShaderFormats BinaryFormats = new ShaderFormats([ShaderFormat.Private, ShaderFormat.SpirV, ShaderFormat.Dxbc, ShaderFormat.Dxil, ShaderFormat.MetalLib]);
    public static readonly ShaderFormats TextFormats = new ShaderFormats([ShaderFormat.Msl, ShaderFormat.Wgsl]);

    private readonly uint _flags;

    public ShaderFormats(uint flags)
    {
        _flags = flags;
    }
    
    // TODO: params
    public ShaderFormats(Span<ShaderFormat> formats)
    {
        foreach (ShaderFormat format in formats)
        {
            _flags |= (uint)format;
        }
    }

    public static ShaderFormats operator &(ShaderFormats a, ShaderFormat b)
    {
        return new ShaderFormats(a._flags & (uint)b);
    }
    
    public static ShaderFormats operator |(ShaderFormats a, ShaderFormat b)
    {
        return new ShaderFormats(a._flags | (uint)b);
    }

    public bool Contains(ShaderFormat format)
    {
        return (_flags & (uint)format) == (uint)format;
    }
}

public class ShaderInstance
{
    public required ShaderFormat Format { get; init; }
    public required string Filename { get; init; }
    public required string EntryPoint { get; init; }
}

internal sealed class GraphicsShaderStageMetadata
{
    public required ShaderBindingLayout BindingLayout { get; init; }
    public ShaderSystemValueInputs SystemValueInputs { get; init; }
    public required List<ShaderInstance> Shaders { get; init; }
}

internal sealed class GraphicsShaderProgramMetadata
{
    public required GraphicsShaderStageMetadata Vertex { get; init; }
    public required GraphicsShaderStageMetadata Fragment { get; init; }
}
