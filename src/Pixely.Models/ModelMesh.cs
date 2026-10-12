using System.Collections.Immutable;
using System.Numerics;
using System.Runtime.InteropServices;
using Pixely.Gpu;

namespace Pixely.Models;

public sealed record ModelMesh(string? Name, ImmutableArray<ModelPrimitive> Primitives);

/// <summary>One draw of a mesh: a triangle list with its own vertex and index buffers.</summary>
public sealed class ModelPrimitive
{
    internal ModelPrimitive(GpuVertexBuffer<PositionTextureNormalVertex> vertices, GpuVertexBuffer<SkinVertex>? skin, GpuIndexBuffer indices, int? material)
    {
        Vertices = vertices;
        Skin = skin;
        Indices = indices;
        Material = material;
    }

    /// <summary>Positions, texture coordinates and normals, for vertex buffer slot 0.</summary>
    public GpuVertexBuffer<PositionTextureNormalVertex> Vertices { get; }

    /// <summary>Joints and weights for the same vertices, for vertex buffer slot 1. Null when the primitive has no <c>JOINTS_0</c>.</summary>
    public GpuVertexBuffer<SkinVertex>? Skin { get; }

    /// <summary>Triangle list indices, 16 or 32 bits wide.</summary>
    public GpuIndexBuffer Indices { get; }

    /// <summary>Index into <see cref="Model.Materials"/>. Null means the glTF default material.</summary>
    public int? Material { get; }
}

/// <summary>The four joints that move a vertex and their weights, which sum to 1.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly record struct SkinVertex(ushort Joint0, ushort Joint1, ushort Joint2, ushort Joint3, Vector4 Weights) : IVertexType
{
    public static ImmutableArray<VertexElementFormat> VertexElements { get; } = [VertexElementFormat.UShort4, VertexElementFormat.Float4];
}
