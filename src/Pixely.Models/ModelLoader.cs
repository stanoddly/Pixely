using System.Collections.Immutable;
using Pixely.Content;
using Pixely.Gpu;
using Pixely.Models.Gltf;

namespace Pixely.Models;

public sealed class ModelLoader : IModelLoader
{
    private readonly ContentSource _contentSource;
    private readonly IImageDecoder _imageDecoder;
    private readonly GpuMemorySystem _gpuMemorySystem;

    public ModelLoader(ContentSource contentSource, IImageDecoder imageDecoder, GpuMemorySystem gpuMemorySystem)
    {
        _contentSource = contentSource;
        _imageDecoder = imageDecoder;
        _gpuMemorySystem = gpuMemorySystem;
    }

    public Model Load(ReadOnlySpan<char> path)
    {
        byte[] file;
        using (Stream stream = _contentSource.OpenStream(path))
        {
            file = new byte[stream.Length];
            stream.ReadExactly(file);
        }

        // The file bytes and the CPU arrays in ModelData are not referenced by the result, so they are released when this returns.
        ModelData data = GltfModelReader.Read(file);

        using ResourceScope scope = new();
        ImmutableArray<ModelMesh> meshes = UploadMeshes(data.Meshes, scope);
        ImmutableArray<ModelMaterial> materials = UploadMaterials(data.Materials, data.Images, scope);

        return new Model(data.Nodes, data.ParentFirstOrder, data.Scenes, data.DefaultScene, meshes, materials, data.Skins, data.Animations, scope.Release());
    }

    private ImmutableArray<ModelMesh> UploadMeshes(ImmutableArray<MeshData> meshData, ResourceScope scope)
    {
        ImmutableArray<ModelMesh>.Builder meshes = ImmutableArray.CreateBuilder<ModelMesh>(meshData.Length);
        foreach (MeshData mesh in meshData)
        {
            ImmutableArray<ModelPrimitive>.Builder primitives = ImmutableArray.CreateBuilder<ModelPrimitive>(mesh.Primitives.Length);
            foreach (PrimitiveData primitive in mesh.Primitives)
            {
                GpuVertexBuffer<PositionTextureNormalVertex> vertices = scope.Add(_gpuMemorySystem.CreateVertexBuffer<PositionTextureNormalVertex>(primitive.Vertices));
                GpuVertexBuffer<SkinVertex>? skin = primitive.Skin is null ? null : scope.Add(_gpuMemorySystem.CreateVertexBuffer<SkinVertex>(primitive.Skin));
                GpuIndexBuffer indices = scope.Add(primitive.Indices16 is { } indices16
                    ? _gpuMemorySystem.CreateIndexBuffer(indices16)
                    : _gpuMemorySystem.CreateIndexBuffer(primitive.Indices32!));
                primitives.Add(new ModelPrimitive(vertices, skin, indices, primitive.Material));
            }

            meshes.Add(new ModelMesh(mesh.Name, primitives.MoveToImmutable()));
        }

        return meshes.MoveToImmutable();
    }

    private ImmutableArray<ModelMaterial> UploadMaterials(ImmutableArray<MaterialData> materialData, ImmutableArray<ReadOnlyMemory<byte>> images, ResourceScope scope)
    {
        // Materials that share an image share its texture.
        Dictionary<int, Texture> textures = new();
        ImmutableArray<ModelMaterial>.Builder materials = ImmutableArray.CreateBuilder<ModelMaterial>(materialData.Length);

        foreach (MaterialData material in materialData)
        {
            ModelTexture? baseColorTexture = null;
            if (material.BaseColorImage is { } imageIndex)
            {
                if (!textures.TryGetValue(imageIndex, out Texture? texture))
                {
                    using Image image = _imageDecoder.Decode(images[imageIndex].Span);
                    // glTF base color textures are sRGB, so sampling returns linear values.
                    texture = scope.Add(_gpuMemorySystem.CreateTexture(image, TextureFormat.R8G8B8A8UnormSrgb));
                    textures.Add(imageIndex, texture);
                }

                baseColorTexture = new ModelTexture(texture, material.BaseColorSampler);
            }

            materials.Add(new ModelMaterial(material.Name, material.BaseColorFactor, baseColorTexture, material.AlphaMode, material.AlphaCutoff, material.DoubleSided));
        }

        return materials.MoveToImmutable();
    }
}
