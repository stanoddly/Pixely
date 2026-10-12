namespace Pixely.Models.Gltf;

// The subset of the glTF 2.0 JSON schema the loader reads. Properties it does not read are ignored by the deserializer.

internal sealed record GltfRootDto
{
    public GltfAssetDto? Asset { get; init; }
    public string[]? ExtensionsRequired { get; init; }
    public int? Scene { get; init; }
    public GltfSceneDto[]? Scenes { get; init; }
    public GltfNodeDto[]? Nodes { get; init; }
    public GltfMeshDto[]? Meshes { get; init; }
    public GltfSkinDto[]? Skins { get; init; }
    public GltfAnimationDto[]? Animations { get; init; }
    public GltfMaterialDto[]? Materials { get; init; }
    public GltfTextureDto[]? Textures { get; init; }
    public GltfImageDto[]? Images { get; init; }
    public GltfSamplerDto[]? Samplers { get; init; }
    public GltfAccessorDto[]? Accessors { get; init; }
    public GltfBufferViewDto[]? BufferViews { get; init; }
    public GltfBufferDto[]? Buffers { get; init; }
}

internal sealed record GltfAssetDto
{
    public string? Version { get; init; }
    public string? MinVersion { get; init; }
}

internal sealed record GltfSceneDto
{
    public string? Name { get; init; }
    public int[]? Nodes { get; init; }
}

internal sealed record GltfNodeDto
{
    public string? Name { get; init; }
    public int[]? Children { get; init; }
    public float[]? Matrix { get; init; }
    public float[]? Translation { get; init; }
    public float[]? Rotation { get; init; }
    public float[]? Scale { get; init; }
    public int? Mesh { get; init; }
    public int? Skin { get; init; }
}

internal sealed record GltfMeshDto
{
    public string? Name { get; init; }
    public GltfPrimitiveDto[]? Primitives { get; init; }
}

internal sealed record GltfPrimitiveDto
{
    public Dictionary<string, int>? Attributes { get; init; }
    public int? Indices { get; init; }
    public int? Material { get; init; }
    public int? Mode { get; init; }
    public Dictionary<string, int>[]? Targets { get; init; }
}

internal sealed record GltfSkinDto
{
    public string? Name { get; init; }
    public int? InverseBindMatrices { get; init; }
    public int? Skeleton { get; init; }
    public int[]? Joints { get; init; }
}

internal sealed record GltfAnimationDto
{
    public string? Name { get; init; }
    public GltfAnimationChannelDto[]? Channels { get; init; }
    public GltfAnimationSamplerDto[]? Samplers { get; init; }
}

internal sealed record GltfAnimationChannelDto
{
    public int? Sampler { get; init; }
    public GltfAnimationTargetDto? Target { get; init; }
}

internal sealed record GltfAnimationTargetDto
{
    public int? Node { get; init; }
    public string? Path { get; init; }
}

internal sealed record GltfAnimationSamplerDto
{
    public int? Input { get; init; }
    public string? Interpolation { get; init; }
    public int? Output { get; init; }
}

internal sealed record GltfMaterialDto
{
    public string? Name { get; init; }
    public GltfPbrMetallicRoughnessDto? PbrMetallicRoughness { get; init; }
    public string? AlphaMode { get; init; }
    public float? AlphaCutoff { get; init; }
    public bool? DoubleSided { get; init; }
}

internal sealed record GltfPbrMetallicRoughnessDto
{
    public float[]? BaseColorFactor { get; init; }
    public GltfTextureInfoDto? BaseColorTexture { get; init; }
}

internal sealed record GltfTextureInfoDto
{
    public int? Index { get; init; }
    public int? TexCoord { get; init; }
}

internal sealed record GltfTextureDto
{
    public int? Sampler { get; init; }
    public int? Source { get; init; }
}

internal sealed record GltfImageDto
{
    public string? Uri { get; init; }
    public string? MimeType { get; init; }
    public int? BufferView { get; init; }
}

internal sealed record GltfSamplerDto
{
    public int? MagFilter { get; init; }
    public int? MinFilter { get; init; }
    public int? WrapS { get; init; }
    public int? WrapT { get; init; }
}

internal sealed record GltfAccessorDto
{
    public int? BufferView { get; init; }
    public long? ByteOffset { get; init; }
    public int? ComponentType { get; init; }
    public bool? Normalized { get; init; }
    public long? Count { get; init; }
    public string? Type { get; init; }
    public GltfSparseDto? Sparse { get; init; }
}

// Only its presence matters: sparse accessors are not supported.
internal sealed record GltfSparseDto
{
    public long? Count { get; init; }
}

internal sealed record GltfBufferViewDto
{
    public int? Buffer { get; init; }
    public long? ByteOffset { get; init; }
    public long? ByteLength { get; init; }
    public int? ByteStride { get; init; }
}

internal sealed record GltfBufferDto
{
    public string? Uri { get; init; }
    public long? ByteLength { get; init; }
}
