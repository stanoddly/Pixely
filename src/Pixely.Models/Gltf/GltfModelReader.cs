using System.Collections.Immutable;
using System.Text.Json;

namespace Pixely.Models.Gltf;

// Reads a GLB file into ModelData. Files are untrusted: every reference, count and value the model uses is checked, and an error
// names the object and its index. The checks cover memory safety and correct output; full conformance is the glTF Validator's job.
internal static class GltfModelReader
{
    public static ModelData Read(ReadOnlyMemory<byte> file)
    {
        try
        {
            return ReadChecked(file);
        }
        catch (OverflowException exception)
        {
            // Offsets and counts come from the file, and size arithmetic is checked, so an overflow means a malformed file.
            throw new InvalidDataException("A size or offset in the file is too large.", exception);
        }
    }

    private static ModelData ReadChecked(ReadOnlyMemory<byte> file)
    {
        GlbChunks chunks = GlbReader.Read(file);
        GltfRootDto root = Deserialize(chunks.Json);

        CheckElements(root);
        CheckAsset(root.Asset);

        if (root.ExtensionsRequired is { Length: > 0 } extensionsRequired)
        {
            throw new NotSupportedException($"The file requires the extensions {string.Join(", ", extensionsRequired)}, which are not supported.");
        }

        ReadOnlyMemory<byte> buffer = GetBuffer(root, chunks.Binary);
        AccessorReader accessors = new(root, buffer, file.Length);

        GltfNodeDto[] nodeDtos = root.Nodes ?? [];
        int[] parents = GltfNodeReader.ReadParents(nodeDtos);
        ImmutableArray<int> parentFirstOrder = GltfNodeReader.ReadParentFirstOrder(nodeDtos, parents);
        ImmutableArray<ModelNode> nodes = GltfNodeReader.ReadNodes(root, nodeDtos, parents);
        ImmutableArray<ModelScene> scenes = GltfNodeReader.ReadScenes(root.Scenes ?? [], nodes, parents, accessors);

        int? defaultScene = root.Scene;
        if (defaultScene is { } sceneIndex && (sceneIndex < 0 || sceneIndex >= scenes.Length))
        {
            throw new InvalidDataException($"scene {sceneIndex} is out of range; the file has {scenes.Length} scenes.");
        }

        ImmutableArray<ModelSkin> skins = GltfSkinReader.ReadSkins(root.Skins ?? [], nodeDtos.Length, accessors);
        ImmutableArray<MeshData> meshes = GltfMeshReader.ReadMeshes(root, accessors);
        CheckSkinnedNodes(nodes, meshes, skins);

        ImmutableArray<MaterialData> materials = GltfMaterialReader.ReadMaterials(root);
        ImmutableArray<ReadOnlyMemory<byte>> images = GltfMaterialReader.ReadImages(root, materials, accessors);
        ImmutableArray<ModelAnimation> animations = GltfAnimationReader.ReadAnimations(root.Animations ?? [], nodeDtos, nodes, accessors);

        return new ModelData(nodes, parentFirstOrder, scenes, defaultScene, meshes, materials, images, skins, animations);
    }

    private static GltfRootDto Deserialize(ReadOnlyMemory<byte> json)
    {
        try
        {
            return JsonSerializer.Deserialize(json.Span, GltfJsonContext.Default.GltfRootDto)
                   ?? throw new InvalidDataException("The JSON chunk is null.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"The JSON chunk is not valid glTF JSON: {exception.Message}", exception);
        }
    }

    // JSON allows null in an array of objects, glTF does not, and the reader uses every element.
    private static void CheckElements(GltfRootDto root)
    {
        CheckElements(root.Scenes, "scenes");
        CheckElements(root.Nodes, "nodes");
        CheckElements(root.Meshes, "meshes");
        CheckElements(root.Skins, "skins");
        CheckElements(root.Animations, "animations");
        CheckElements(root.Materials, "materials");
        CheckElements(root.Textures, "textures");
        CheckElements(root.Images, "images");
        CheckElements(root.Samplers, "samplers");
        CheckElements(root.Accessors, "accessors");
        CheckElements(root.BufferViews, "bufferViews");
        CheckElements(root.Buffers, "buffers");

        for (int index = 0; index < (root.Meshes?.Length ?? 0); index++)
        {
            CheckElements(root.Meshes![index].Primitives, $"meshes[{index}].primitives");
        }

        for (int index = 0; index < (root.Animations?.Length ?? 0); index++)
        {
            CheckElements(root.Animations![index].Channels, $"animations[{index}].channels");
            CheckElements(root.Animations[index].Samplers, $"animations[{index}].samplers");
        }
    }

    private static void CheckElements<T>(T?[]? elements, string name) where T : class
    {
        int index = elements is null ? -1 : Array.IndexOf(elements, null);
        if (index >= 0)
        {
            throw new InvalidDataException($"{name}[{index}] is null.");
        }
    }

    private static void CheckAsset(GltfAssetDto? asset)
    {
        if (asset?.Version is not { } versionText)
        {
            throw new InvalidDataException("asset.version is missing.");
        }

        Version version = ParseVersion(versionText, "asset.version");
        if (version.Major != 2)
        {
            throw new NotSupportedException($"asset.version is {versionText}; only glTF 2.x is supported.");
        }

        if (asset.MinVersion is { } minVersionText)
        {
            Version minVersion = ParseVersion(minVersionText, "asset.minVersion");
            if (minVersion > version)
            {
                throw new InvalidDataException($"asset.minVersion {minVersionText} is greater than asset.version {versionText}.");
            }

            if (minVersion > new Version(2, 0))
            {
                throw new NotSupportedException($"asset.minVersion is {minVersionText}; only files readable by glTF 2.0 are supported.");
            }
        }
    }

    private static Version ParseVersion(string text, string name)
    {
        string[] parts = text.Split('.');
        if (parts.Length != 2 || !int.TryParse(parts[0], out int major) || !int.TryParse(parts[1], out int minor) || major < 0 || minor < 0)
        {
            throw new InvalidDataException($"{name} '{text}' is not of the form major.minor.");
        }

        return new Version(major, minor);
    }

    private static ReadOnlyMemory<byte> GetBuffer(GltfRootDto root, ReadOnlyMemory<byte>? binaryChunk)
    {
        GltfBufferDto[] buffers = root.Buffers ?? [];
        if (buffers.Length == 0)
        {
            return ReadOnlyMemory<byte>.Empty;
        }

        if (buffers.Length > 1)
        {
            throw new NotSupportedException($"The file has {buffers.Length} buffers; only the GLB binary chunk is supported.");
        }

        GltfBufferDto buffer = buffers[0];
        if (buffer.Uri is not null)
        {
            throw new NotSupportedException("buffers[0] has a uri; external and data URI buffers are not supported.");
        }

        long byteLength = buffer.ByteLength ?? throw new InvalidDataException("buffers[0]: byteLength is missing.");
        if (byteLength < 1)
        {
            throw new InvalidDataException($"buffers[0]: byteLength {byteLength} is out of range.");
        }

        if (binaryChunk is not { } binary)
        {
            throw new InvalidDataException("buffers[0] refers to the GLB binary chunk, but the file has none.");
        }

        if (binary.Length < byteLength)
        {
            throw new InvalidDataException($"buffers[0] is {byteLength} bytes, but the binary chunk has only {binary.Length}.");
        }

        return binary[..(int)byteLength];
    }

    private static void CheckSkinnedNodes(ImmutableArray<ModelNode> nodes, ImmutableArray<MeshData> meshes, ImmutableArray<ModelSkin> skins)
    {
        for (int index = 0; index < nodes.Length; index++)
        {
            if (nodes[index] is not { Skin: { } skin, Mesh: { } mesh })
            {
                continue;
            }

            foreach (PrimitiveData primitive in meshes[mesh].Primitives)
            {
                if (primitive.Skin is null)
                {
                    throw new InvalidDataException($"nodes[{index}]: has skin {skin}, but a primitive of mesh {mesh} has no JOINTS_0 and WEIGHTS_0.");
                }

                if (primitive.MaxJoint >= skins[skin].Joints.Length)
                {
                    throw new InvalidDataException($"nodes[{index}]: mesh {mesh} uses joint {primitive.MaxJoint}, but skin {skin} has {skins[skin].Joints.Length} joints.");
                }
            }
        }
    }
}
