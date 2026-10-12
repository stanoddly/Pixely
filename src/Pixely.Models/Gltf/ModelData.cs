using System.Collections.Immutable;
using System.Numerics;
using Pixely.Gpu;

namespace Pixely.Models.Gltf;

// The model as read from the file, before anything is uploaded to the GPU.
internal sealed record ModelData(
    ImmutableArray<ModelNode> Nodes,
    ImmutableArray<int> ParentFirstOrder,
    ImmutableArray<ModelScene> Scenes,
    int? DefaultScene,
    ImmutableArray<MeshData> Meshes,
    ImmutableArray<MaterialData> Materials,
    ImmutableArray<ReadOnlyMemory<byte>> Images,
    ImmutableArray<ModelSkin> Skins,
    ImmutableArray<ModelAnimation> Animations);

internal sealed record MeshData(string? Name, ImmutableArray<PrimitiveData> Primitives);

// Exactly one of Indices16 and Indices32 is set. MaxJoint is -1 without a skin. Primitives that read the same accessors share arrays.
internal sealed record PrimitiveData(PositionTextureNormalVertex[] Vertices, SkinVertex[]? Skin, ushort[]? Indices16, uint[]? Indices32, int? Material, int MaxJoint);

// BaseColorImage indexes ModelData.Images, which holds the encoded bytes of every image a material uses. Images with the same buffer
// view bytes compare equal.
internal sealed record MaterialData(string? Name, Vector4 BaseColorFactor, int? BaseColorImage, SamplerConfig BaseColorSampler, AlphaMode AlphaMode, float AlphaCutoff, bool DoubleSided);
