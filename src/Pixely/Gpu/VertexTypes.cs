using System.Collections.Immutable;
using System.Numerics;
using System.Runtime.InteropServices;
using SDL;

namespace Pixely.Gpu;

public enum VertexElementFormat
{
    Invalid = SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_INVALID,
    Int = SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_INT,
    Int2 = SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_INT2,
    Int3 = SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_INT3,
    Int4 = SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_INT4,
    UInt = SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_UINT,
    UInt2 = SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_UINT2,
    UInt3 = SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_UINT3,
    UInt4 = SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_UINT4,
    Float = SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_FLOAT,
    Float2 = SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_FLOAT2,
    Float3 = SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_FLOAT3,
    Float4 = SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_FLOAT4,
    Byte2 = SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_BYTE2,
    Byte4 = SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_BYTE4,
    UByte2 = SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_UBYTE2,
    UByte4 = SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_UBYTE4,
    Byte2Norm = SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_BYTE2_NORM,
    Byte4Norm = SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_BYTE4_NORM,
    UByte2Norm = SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_UBYTE2_NORM,
    UByte4Norm = SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_UBYTE4_NORM,
    Short2 = SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_SHORT2,
    Short4 = SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_SHORT4,
    UShort2 = SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_USHORT2,
    UShort4 = SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_USHORT4,
    Short2Norm = SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_SHORT2_NORM,
    Short4Norm = SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_SHORT4_NORM,
    UShort2Norm = SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_USHORT2_NORM,
    UShort4Norm = SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_USHORT4_NORM,
    Half2 = SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_HALF2,
    Half4 = SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_HALF4
}

public static class VertexElementFormatExtensions
{
    public static int GetNumberOfBytes(this VertexElementFormat format)
    {
        return format switch
        {
            VertexElementFormat.Int => 4,
            VertexElementFormat.Int2 => 8,
            VertexElementFormat.Int3 => 12,
            VertexElementFormat.Int4 => 16,
            VertexElementFormat.UInt => 4,
            VertexElementFormat.UInt2 => 8,
            VertexElementFormat.UInt3 => 12,
            VertexElementFormat.UInt4 => 16,
            VertexElementFormat.Float => 4,
            VertexElementFormat.Float2 => 8,
            VertexElementFormat.Float3 => 12,
            VertexElementFormat.Float4 => 16,
            VertexElementFormat.Byte2 => 2,
            VertexElementFormat.Byte4 => 4,
            VertexElementFormat.UByte2 => 2,
            VertexElementFormat.UByte4 => 4,
            VertexElementFormat.Byte2Norm => 2,
            VertexElementFormat.Byte4Norm => 4,
            VertexElementFormat.UByte2Norm => 2,
            VertexElementFormat.UByte4Norm => 4,
            VertexElementFormat.Short2 => 4,
            VertexElementFormat.Short4 => 8,
            VertexElementFormat.UShort2 => 4,
            VertexElementFormat.UShort4 => 8,
            VertexElementFormat.Short2Norm => 4,
            VertexElementFormat.Short4Norm => 8,
            VertexElementFormat.UShort2Norm => 4,
            VertexElementFormat.UShort4Norm => 8,
            VertexElementFormat.Half2 => 4,
            VertexElementFormat.Half4 => 8,
            _ => throw new ArgumentException("Invalid or unsupported vertex element format.")
        };
    }
}

public interface IVertexType
{
    static abstract ImmutableArray<VertexElementFormat> VertexElements { get; }
}



public interface IPositionable
{
    Vector3 Position { get; init; }
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly record struct PositionVertex(Vector3 Position) : IVertexType, IPositionable
{
    public static implicit operator PositionVertex(Vector3 position) => new PositionVertex(position);

    public static ImmutableArray<VertexElementFormat> VertexElements { get; } = [VertexElementFormat.Float3];
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly record struct PositionColorVertex(Vector3 Position, SrgbColor Color) : IVertexType, IPositionable
{
    public static implicit operator PositionColorVertex((Vector3 position, SrgbColor color) tuple) => new PositionColorVertex(tuple.position, tuple.color);

    public static ImmutableArray<VertexElementFormat> VertexElements { get; } = [VertexElementFormat.Float3, VertexElementFormat.UByte4Norm];
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly record struct PositionTextureVertex(Vector3 Position, Vector2 TextureCoords) : IVertexType, IPositionable
{
    public static implicit operator PositionTextureVertex((Vector3 position, Vector2 textureCoords) tuple) => new PositionTextureVertex(tuple.position, tuple.textureCoords);

    public static ImmutableArray<VertexElementFormat> VertexElements { get; } = [VertexElementFormat.Float3, VertexElementFormat.Float2];
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly record struct PositionNormalColorVertex(Vector3 Position, Vector3 Normal, SrgbColor Color) : IVertexType, IPositionable
{
    public static implicit operator PositionNormalColorVertex((Vector3 position, Vector3 normal, SrgbColor color) tuple) => new PositionNormalColorVertex(tuple.position, tuple.normal, tuple.color);

    public static ImmutableArray<VertexElementFormat> VertexElements { get; } = [VertexElementFormat.Float3, VertexElementFormat.Float3, VertexElementFormat.UByte4Norm];
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly record struct PositionTextureNormalVertex(Vector3 Position, Vector2 TextureCoords, Vector3 Normal) : IVertexType, IPositionable
{
    public static implicit operator PositionTextureNormalVertex((Vector3 position, Vector3 normal, Vector2 textureCoords) tuple) => new PositionTextureNormalVertex(tuple.position, tuple.textureCoords, tuple.normal);
    public static ImmutableArray<VertexElementFormat> VertexElements { get; } = [VertexElementFormat.Float3, VertexElementFormat.Float2, VertexElementFormat.Float3];
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly record struct PositionTextureNormalColorVertex(Vector3 Position, Vector2 TextureCoords, Vector3 Normal, SrgbColor Color) : IVertexType, IPositionable
{
    public static implicit operator PositionTextureNormalColorVertex((Vector3 position, Vector2 textureCoords, Vector3 normal, SrgbColor color) tuple) => new PositionTextureNormalColorVertex(tuple.position, tuple.textureCoords, tuple.normal, tuple.color);
    public static ImmutableArray<VertexElementFormat> VertexElements { get; } = [VertexElementFormat.Float3, VertexElementFormat.Float2, VertexElementFormat.Float3, VertexElementFormat.UByte4Norm];
}