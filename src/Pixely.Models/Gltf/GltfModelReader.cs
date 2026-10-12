using System.Collections.Immutable;
using System.Numerics;
using System.Text.Json;
using Pixely.Gpu;

namespace Pixely.Models.Gltf;

// Reads a GLB file into ModelData. Files are untrusted: every reference, count and value the model uses is checked, and an error
// names the object and its index. The checks cover memory safety and correct output; full conformance is the glTF Validator's job.
internal static class GltfModelReader
{
    private const int TrianglesMode = 4;
    private const float MatrixTolerance = 1e-4f;
    private const float AffineTolerance = 1e-5f;
    private const float MinimumLength = 1e-12f;

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

        CheckAsset(root.Asset);

        if (root.ExtensionsRequired is { Length: > 0 } extensionsRequired)
        {
            throw new NotSupportedException($"The file requires the extensions {string.Join(", ", extensionsRequired)}, which are not supported.");
        }

        ReadOnlyMemory<byte> buffer = GetBuffer(root, chunks.Binary);
        AccessorReader accessors = new(root, buffer, file.Length);

        GltfNodeDto[] nodeDtos = root.Nodes ?? [];
        int[] parents = ReadParents(nodeDtos);
        ImmutableArray<int> parentFirstOrder = ReadParentFirstOrder(nodeDtos, parents);
        ImmutableArray<ModelNode> nodes = ReadNodes(root, nodeDtos, parents);
        ImmutableArray<ModelScene> scenes = ReadScenes(root.Scenes ?? [], parents);

        int? defaultScene = root.Scene;
        if (defaultScene is { } sceneIndex && (sceneIndex < 0 || sceneIndex >= scenes.Length))
        {
            throw new InvalidDataException($"scene {sceneIndex} is out of range; the file has {scenes.Length} scenes.");
        }

        ImmutableArray<ModelSkin> skins = ReadSkins(root.Skins ?? [], nodeDtos.Length, accessors);
        ImmutableArray<MeshData> meshes = ReadMeshes(root, accessors);
        CheckSkinnedNodes(nodes, meshes, skins);

        ImmutableArray<MaterialData> materials = ReadMaterials(root);
        ImmutableArray<ReadOnlyMemory<byte>> images = ReadImages(root, materials, accessors);
        ImmutableArray<ModelAnimation> animations = ReadAnimations(root.Animations ?? [], nodeDtos, accessors);

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

    private static int[] ReadParents(GltfNodeDto[] nodes)
    {
        int[] parents = new int[nodes.Length];
        Array.Fill(parents, -1);

        for (int node = 0; node < nodes.Length; node++)
        {
            foreach (int child in nodes[node].Children ?? [])
            {
                if (child < 0 || child >= nodes.Length)
                {
                    throw new InvalidDataException($"nodes[{node}]: child {child} is out of range.");
                }

                if (parents[child] != -1 || child == node)
                {
                    throw new InvalidDataException($"nodes[{child}] has more than one parent, or is its own child.");
                }

                parents[child] = node;
            }
        }

        return parents;
    }

    // Every node has at most one parent here, so a node that no walk from the roots reaches is part of a cycle.
    private static ImmutableArray<int> ReadParentFirstOrder(GltfNodeDto[] nodes, int[] parents)
    {
        ImmutableArray<int>.Builder order = ImmutableArray.CreateBuilder<int>(nodes.Length);
        Stack<int> pending = new();

        for (int node = 0; node < nodes.Length; node++)
        {
            if (parents[node] != -1)
            {
                continue;
            }

            pending.Push(node);
            while (pending.Count > 0)
            {
                int current = pending.Pop();
                order.Add(current);
                foreach (int child in nodes[current].Children ?? [])
                {
                    pending.Push(child);
                }
            }
        }

        if (order.Count != nodes.Length)
        {
            throw new InvalidDataException("The node hierarchy has a cycle.");
        }

        return order.MoveToImmutable();
    }

    private static ImmutableArray<ModelNode> ReadNodes(GltfRootDto root, GltfNodeDto[] nodeDtos, int[] parents)
    {
        int meshCount = root.Meshes?.Length ?? 0;
        int skinCount = root.Skins?.Length ?? 0;
        ImmutableArray<ModelNode>.Builder nodes = ImmutableArray.CreateBuilder<ModelNode>(nodeDtos.Length);

        for (int index = 0; index < nodeDtos.Length; index++)
        {
            GltfNodeDto node = nodeDtos[index];
            string name = $"nodes[{index}]";

            if (node.Mesh is { } mesh && (mesh < 0 || mesh >= meshCount))
            {
                throw new InvalidDataException($"{name}: mesh {mesh} is out of range.");
            }

            if (node.Skin is { } skin && (skin < 0 || skin >= skinCount))
            {
                throw new InvalidDataException($"{name}: skin {skin} is out of range.");
            }

            if (node.Skin is not null && node.Mesh is null)
            {
                throw new InvalidDataException($"{name}: a node with a skin needs a mesh.");
            }

            int? parent = parents[index] == -1 ? null : parents[index];
            nodes.Add(new ModelNode(node.Name, parent, [.. node.Children ?? []], ReadTransform(node, name), node.Mesh, node.Skin));
        }

        return nodes.MoveToImmutable();
    }

    private static NodeTransform ReadTransform(GltfNodeDto node, string name)
    {
        if (node.Matrix is { } matrixValues)
        {
            if (node.Translation is not null || node.Rotation is not null || node.Scale is not null)
            {
                throw new InvalidDataException($"{name}: a node with a matrix cannot also have translation, rotation or scale.");
            }

            if (matrixValues.Length != 16)
            {
                throw new InvalidDataException($"{name}: matrix has {matrixValues.Length} values instead of 16.");
            }

            Matrix4x4 matrix = ReadMatrix(matrixValues, 0, name, "matrix");

            // glTF requires a node matrix to be decomposable to TRS; recomposing catches shear and perspective, which Decompose
            // can accept but NodeTransform cannot represent.
            if (!Matrix4x4.Decompose(matrix, out Vector3 scale, out Quaternion rotation, out Vector3 translation))
            {
                throw new InvalidDataException($"{name}: matrix cannot be decomposed into translation, rotation and scale.");
            }

            NodeTransform transform = new(translation, Quaternion.Normalize(rotation), scale);
            if (!AreClose(transform.ToMatrix(), matrix, MatrixTolerance))
            {
                throw new InvalidDataException($"{name}: matrix has shear or perspective, which cannot be decomposed into translation, rotation and scale.");
            }

            return transform;
        }

        Vector3 nodeTranslation = node.Translation is { } t ? ReadVector3(t, name, "translation") : Vector3.Zero;
        Vector3 nodeScale = node.Scale is { } s ? ReadVector3(s, name, "scale") : Vector3.One;
        Quaternion nodeRotation = Quaternion.Identity;
        if (node.Rotation is { } r)
        {
            if (r.Length != 4)
            {
                throw new InvalidDataException($"{name}: rotation has {r.Length} values instead of 4.");
            }

            nodeRotation = NormalizeRotation(new Vector4(r[0], r[1], r[2], r[3]), name);
        }

        return new NodeTransform(nodeTranslation, nodeRotation, nodeScale);
    }

    private static ImmutableArray<ModelScene> ReadScenes(GltfSceneDto[] sceneDtos, int[] parents)
    {
        ImmutableArray<ModelScene>.Builder scenes = ImmutableArray.CreateBuilder<ModelScene>(sceneDtos.Length);
        for (int index = 0; index < sceneDtos.Length; index++)
        {
            int[] roots = sceneDtos[index].Nodes ?? [];
            foreach (int root in roots)
            {
                if (root < 0 || root >= parents.Length)
                {
                    throw new InvalidDataException($"scenes[{index}]: node {root} is out of range.");
                }

                if (parents[root] != -1)
                {
                    throw new InvalidDataException($"scenes[{index}]: node {root} is a root of the scene but has a parent.");
                }
            }

            scenes.Add(new ModelScene(sceneDtos[index].Name, [.. roots]));
        }

        return scenes.MoveToImmutable();
    }

    private static ImmutableArray<ModelSkin> ReadSkins(GltfSkinDto[] skinDtos, int nodeCount, AccessorReader accessors)
    {
        ImmutableArray<ModelSkin>.Builder skins = ImmutableArray.CreateBuilder<ModelSkin>(skinDtos.Length);
        for (int index = 0; index < skinDtos.Length; index++)
        {
            GltfSkinDto skin = skinDtos[index];
            string name = $"skins[{index}]";
            int[] joints = skin.Joints ?? [];

            if (joints.Length == 0)
            {
                throw new InvalidDataException($"{name}: joints is empty.");
            }

            HashSet<int> seen = new();
            foreach (int joint in joints)
            {
                if (joint < 0 || joint >= nodeCount)
                {
                    throw new InvalidDataException($"{name}: joint {joint} is out of range.");
                }

                if (!seen.Add(joint))
                {
                    throw new InvalidDataException($"{name}: joint {joint} is listed twice.");
                }
            }

            if (skin.Skeleton is { } skeleton && (skeleton < 0 || skeleton >= nodeCount))
            {
                throw new InvalidDataException($"{name}: skeleton {skeleton} is out of range.");
            }

            ImmutableArray<Matrix4x4>.Builder inverseBindMatrices = ImmutableArray.CreateBuilder<Matrix4x4>(joints.Length);
            if (skin.InverseBindMatrices is { } accessor)
            {
                float[] values = accessors.ReadFloats(accessor, AccessorUse.InverseBindMatrices);
                if (values.Length / 16 < joints.Length)
                {
                    throw new InvalidDataException($"{name}: inverseBindMatrices has {values.Length / 16} matrices for {joints.Length} joints.");
                }

                // Extra matrices beyond the joint count are allowed and unused.
                for (int joint = 0; joint < joints.Length; joint++)
                {
                    Matrix4x4 matrix = ReadMatrix(values, joint * 16, name, $"inverseBindMatrices[{joint}]");
                    if (MathF.Abs(matrix.M14) > AffineTolerance || MathF.Abs(matrix.M24) > AffineTolerance || MathF.Abs(matrix.M34) > AffineTolerance
                        || MathF.Abs(matrix.M44 - 1f) > AffineTolerance)
                    {
                        throw new InvalidDataException($"{name}: inverseBindMatrices[{joint}] is not affine.");
                    }

                    inverseBindMatrices.Add(matrix);
                }
            }
            else
            {
                for (int joint = 0; joint < joints.Length; joint++)
                {
                    inverseBindMatrices.Add(Matrix4x4.Identity);
                }
            }

            skins.Add(new ModelSkin(skin.Name, [.. joints], inverseBindMatrices.MoveToImmutable(), skin.Skeleton));
        }

        return skins.MoveToImmutable();
    }

    private static ImmutableArray<MeshData> ReadMeshes(GltfRootDto root, AccessorReader accessors)
    {
        GltfMeshDto[] meshDtos = root.Meshes ?? [];
        int materialCount = root.Materials?.Length ?? 0;
        ImmutableArray<MeshData>.Builder meshes = ImmutableArray.CreateBuilder<MeshData>(meshDtos.Length);

        for (int meshIndex = 0; meshIndex < meshDtos.Length; meshIndex++)
        {
            GltfPrimitiveDto[] primitiveDtos = meshDtos[meshIndex].Primitives ?? [];
            if (primitiveDtos.Length == 0)
            {
                throw new InvalidDataException($"meshes[{meshIndex}]: primitives is empty.");
            }

            ImmutableArray<PrimitiveData>.Builder primitives = ImmutableArray.CreateBuilder<PrimitiveData>(primitiveDtos.Length);
            for (int primitiveIndex = 0; primitiveIndex < primitiveDtos.Length; primitiveIndex++)
            {
                string name = $"meshes[{meshIndex}].primitives[{primitiveIndex}]";
                primitives.Add(ReadPrimitive(primitiveDtos[primitiveIndex], name, materialCount, accessors));
            }

            meshes.Add(new MeshData(meshDtos[meshIndex].Name, primitives.MoveToImmutable()));
        }

        return meshes.MoveToImmutable();
    }

    private static PrimitiveData ReadPrimitive(GltfPrimitiveDto primitive, string name, int materialCount, AccessorReader accessors)
    {
        int mode = primitive.Mode ?? TrianglesMode;
        if (mode != TrianglesMode)
        {
            throw new NotSupportedException($"{name}: primitive mode {mode} is not supported; only triangles (4) are.");
        }

        if (primitive.Targets is { Length: > 0 })
        {
            throw new NotSupportedException($"{name}: morph targets are not supported.");
        }

        if (primitive.Material is { } material && (material < 0 || material >= materialCount))
        {
            throw new InvalidDataException($"{name}: material {material} is out of range.");
        }

        Dictionary<string, int> attributes = primitive.Attributes ?? new Dictionary<string, int>();
        foreach (string attribute in attributes.Keys)
        {
            if ((attribute.StartsWith("JOINTS_", StringComparison.Ordinal) || attribute.StartsWith("WEIGHTS_", StringComparison.Ordinal))
                && attribute is not ("JOINTS_0" or "WEIGHTS_0"))
            {
                throw new NotSupportedException($"{name}: {attribute} is not supported; only one set of joints and weights is.");
            }
        }

        if (!attributes.TryGetValue("POSITION", out int positionAccessor))
        {
            throw new NotSupportedException($"{name}: primitives without POSITION are not supported.");
        }

        if (!attributes.TryGetValue("NORMAL", out int normalAccessor))
        {
            throw new NotSupportedException($"{name}: primitives without NORMAL are not supported.");
        }

        bool hasJoints = attributes.TryGetValue("JOINTS_0", out int jointsAccessor);
        bool hasWeights = attributes.TryGetValue("WEIGHTS_0", out int weightsAccessor);
        if (hasJoints != hasWeights)
        {
            throw new InvalidDataException($"{name}: JOINTS_0 and WEIGHTS_0 must be both present or both absent.");
        }

        float[] positions = accessors.ReadFloats(positionAccessor, AccessorUse.Position);
        int vertexCount = positions.Length / 3;

        float[] normals = accessors.ReadFloats(normalAccessor, AccessorUse.Normal);
        CheckCount(name, "NORMAL", normals.Length / 3, vertexCount);

        float[]? texCoords = null;
        if (attributes.TryGetValue("TEXCOORD_0", out int texCoordAccessor))
        {
            texCoords = accessors.ReadFloats(texCoordAccessor, AccessorUse.TexCoord);
            CheckCount(name, "TEXCOORD_0", texCoords.Length / 2, vertexCount);
        }

        PositionTextureNormalVertex[] vertices = new PositionTextureNormalVertex[vertexCount];
        for (int vertex = 0; vertex < vertexCount; vertex++)
        {
            Vector3 position = new(positions[vertex * 3], positions[vertex * 3 + 1], positions[vertex * 3 + 2]);
            Vector3 normal = new(normals[vertex * 3], normals[vertex * 3 + 1], normals[vertex * 3 + 2]);
            float normalLength = normal.Length();
            if (normalLength < MinimumLength)
            {
                throw new InvalidDataException($"{name}: the normal of vertex {vertex} has zero length.");
            }

            // Exporters often write normals that are not quite unit length, so they are repaired instead of rejected.
            Vector2 texCoord = texCoords is null ? Vector2.Zero : new Vector2(texCoords[vertex * 2], texCoords[vertex * 2 + 1]);
            vertices[vertex] = new PositionTextureNormalVertex(position, texCoord, normal / normalLength);
        }

        SkinVertex[]? skin = null;
        int maxJoint = -1;
        if (hasJoints)
        {
            uint[] joints = accessors.ReadIntegers(jointsAccessor, AccessorUse.Joints).Values;
            CheckCount(name, "JOINTS_0", joints.Length / 4, vertexCount);
            float[] weights = accessors.ReadFloats(weightsAccessor, AccessorUse.Weights);
            CheckCount(name, "WEIGHTS_0", weights.Length / 4, vertexCount);

            skin = new SkinVertex[vertexCount];
            for (int vertex = 0; vertex < vertexCount; vertex++)
            {
                Vector4 weight = new(weights[vertex * 4], weights[vertex * 4 + 1], weights[vertex * 4 + 2], weights[vertex * 4 + 3]);
                if (weight.X < 0 || weight.Y < 0 || weight.Z < 0 || weight.W < 0)
                {
                    throw new InvalidDataException($"{name}: vertex {vertex} has a negative weight.");
                }

                float sum = weight.X + weight.Y + weight.Z + weight.W;
                if (sum < MinimumLength)
                {
                    throw new InvalidDataException($"{name}: the weights of vertex {vertex} sum to 0.");
                }

                // Quantized weights rarely sum to exactly 1, and exporters often miss it, so the sum is repaired instead of checked.
                // A joint repeated within a vertex is kept: the blend adds its weights, which equals one combined weight.
                ushort joint0 = (ushort)joints[vertex * 4];
                ushort joint1 = (ushort)joints[vertex * 4 + 1];
                ushort joint2 = (ushort)joints[vertex * 4 + 2];
                ushort joint3 = (ushort)joints[vertex * 4 + 3];
                maxJoint = Math.Max(maxJoint, Math.Max(Math.Max(joint0, joint1), Math.Max(joint2, joint3)));
                skin[vertex] = new SkinVertex(joint0, joint1, joint2, joint3, weight / sum);
            }
        }

        ushort[]? indices16 = null;
        uint[]? indices32 = null;
        if (primitive.Indices is { } indexAccessor)
        {
            IntegerAccessorData indexData = accessors.ReadIntegers(indexAccessor, AccessorUse.Indices);
            uint[] indices = indexData.Values;
            if (indices.Length % 3 != 0)
            {
                throw new InvalidDataException($"{name}: {indices.Length} indices do not form whole triangles.");
            }

            foreach (uint index in indices)
            {
                if (index >= vertexCount)
                {
                    throw new InvalidDataException($"{name}: index {index} is out of range for {vertexCount} vertices.");
                }
            }

            if (indexData.ComponentType == AccessorReader.UnsignedInt)
            {
                indices32 = indices;
            }
            else
            {
                indices16 = Array.ConvertAll(indices, index => (ushort)index);
            }
        }
        else
        {
            if (vertexCount % 3 != 0)
            {
                throw new InvalidDataException($"{name}: {vertexCount} vertices without indices do not form whole triangles.");
            }

            // 16-bit indices address 65,536 vertices, 0 to 65,535.
            if (vertexCount <= ushort.MaxValue + 1)
            {
                indices16 = new ushort[vertexCount];
                for (int i = 0; i < vertexCount; i++)
                {
                    indices16[i] = (ushort)i;
                }
            }
            else
            {
                indices32 = new uint[vertexCount];
                for (int i = 0; i < vertexCount; i++)
                {
                    indices32[i] = (uint)i;
                }
            }
        }

        return new PrimitiveData(vertices, skin, indices16, indices32, primitive.Material, maxJoint);
    }

    private static void CheckCount(string name, string attribute, int count, int vertexCount)
    {
        if (count != vertexCount)
        {
            throw new InvalidDataException($"{name}: {attribute} has {count} elements, but POSITION has {vertexCount}.");
        }
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

    private static ImmutableArray<MaterialData> ReadMaterials(GltfRootDto root)
    {
        GltfMaterialDto[] materialDtos = root.Materials ?? [];
        GltfTextureDto[] textures = root.Textures ?? [];
        GltfSamplerDto[] samplers = root.Samplers ?? [];
        int imageCount = root.Images?.Length ?? 0;
        ImmutableArray<MaterialData>.Builder materials = ImmutableArray.CreateBuilder<MaterialData>(materialDtos.Length);

        for (int index = 0; index < materialDtos.Length; index++)
        {
            GltfMaterialDto material = materialDtos[index];
            string name = $"materials[{index}]";

            Vector4 baseColorFactor = Vector4.One;
            if (material.PbrMetallicRoughness?.BaseColorFactor is { } factor)
            {
                if (factor.Length != 4 || !factor.All(float.IsFinite))
                {
                    throw new InvalidDataException($"{name}: baseColorFactor must be 4 finite numbers.");
                }

                baseColorFactor = new Vector4(factor[0], factor[1], factor[2], factor[3]);
            }

            int? baseColorImage = null;
            SamplerConfig sampler = GltfSamplers.Default;
            if (material.PbrMetallicRoughness?.BaseColorTexture is { } textureInfo)
            {
                int textureIndex = textureInfo.Index ?? throw new InvalidDataException($"{name}: baseColorTexture.index is missing.");
                if (textureIndex < 0 || textureIndex >= textures.Length)
                {
                    throw new InvalidDataException($"{name}: texture {textureIndex} is out of range.");
                }

                if ((textureInfo.TexCoord ?? 0) != 0)
                {
                    throw new NotSupportedException($"{name}: baseColorTexture uses TEXCOORD_{textureInfo.TexCoord}; only TEXCOORD_0 is supported.");
                }

                GltfTextureDto texture = textures[textureIndex];
                int source = texture.Source ?? throw new NotSupportedException($"textures[{textureIndex}] has no source; textures defined only by extensions are not supported.");
                if (source < 0 || source >= imageCount)
                {
                    throw new InvalidDataException($"textures[{textureIndex}]: source {source} is out of range.");
                }

                if (texture.Sampler is { } samplerIndex)
                {
                    if (samplerIndex < 0 || samplerIndex >= samplers.Length)
                    {
                        throw new InvalidDataException($"textures[{textureIndex}]: sampler {samplerIndex} is out of range.");
                    }

                    sampler = GltfSamplers.ToSamplerConfig(samplers[samplerIndex], $"samplers[{samplerIndex}]");
                }

                baseColorImage = source;
            }

            AlphaMode alphaMode = material.AlphaMode switch
            {
                null or "OPAQUE" => AlphaMode.Opaque,
                "MASK" => AlphaMode.Mask,
                "BLEND" => AlphaMode.Blend,
                _ => throw new InvalidDataException($"{name}: alphaMode '{material.AlphaMode}' is not OPAQUE, MASK or BLEND.")
            };

            float alphaCutoff = material.AlphaCutoff ?? 0.5f;
            if (!float.IsFinite(alphaCutoff) || alphaCutoff < 0)
            {
                throw new InvalidDataException($"{name}: alphaCutoff {alphaCutoff} is out of range.");
            }

            materials.Add(new MaterialData(material.Name, baseColorFactor, baseColorImage, sampler, alphaMode, alphaCutoff, material.DoubleSided ?? false));
        }

        return materials.MoveToImmutable();
    }

    // Only images a material uses are read, so an unused image the loader cannot read, such as an external normal map, does not
    // fail the load. Unused entries stay empty.
    private static ImmutableArray<ReadOnlyMemory<byte>> ReadImages(GltfRootDto root, ImmutableArray<MaterialData> materials, AccessorReader accessors)
    {
        GltfImageDto[] imageDtos = root.Images ?? [];
        ReadOnlyMemory<byte>[] images = new ReadOnlyMemory<byte>[imageDtos.Length];

        foreach (MaterialData material in materials)
        {
            if (material.BaseColorImage is not { } index || !images[index].IsEmpty)
            {
                continue;
            }

            GltfImageDto image = imageDtos[index];
            if (image.Uri is not null)
            {
                throw new NotSupportedException($"images[{index}] has a uri; external and data URI images are not supported.");
            }

            int bufferView = image.BufferView ?? throw new InvalidDataException($"images[{index}] has neither a uri nor a bufferView.");
            if (image.MimeType is not ("image/png" or "image/jpeg"))
            {
                throw new NotSupportedException($"images[{index}]: mimeType '{image.MimeType}' is not supported; only image/png and image/jpeg are.");
            }

            images[index] = accessors.GetBufferView(bufferView, out _);
        }

        return [.. images];
    }

    private static ImmutableArray<ModelAnimation> ReadAnimations(GltfAnimationDto[] animationDtos, GltfNodeDto[] nodes, AccessorReader accessors)
    {
        ImmutableArray<ModelAnimation>.Builder animations = ImmutableArray.CreateBuilder<ModelAnimation>(animationDtos.Length);

        for (int animationIndex = 0; animationIndex < animationDtos.Length; animationIndex++)
        {
            GltfAnimationDto animation = animationDtos[animationIndex];
            string animationName = $"animations[{animationIndex}]";
            GltfAnimationChannelDto[] channelDtos = animation.Channels ?? [];
            GltfAnimationSamplerDto[] samplers = animation.Samplers ?? [];
            HashSet<(int Node, AnimationPath Path)> targets = new();
            ImmutableArray<AnimationChannel>.Builder channels = ImmutableArray.CreateBuilder<AnimationChannel>();

            for (int channelIndex = 0; channelIndex < channelDtos.Length; channelIndex++)
            {
                GltfAnimationChannelDto channel = channelDtos[channelIndex];
                string name = $"{animationName}.channels[{channelIndex}]";
                GltfAnimationTargetDto target = channel.Target ?? throw new InvalidDataException($"{name}: target is missing.");

                // A target without a node is valid glTF, for extensions to animate other properties; there is nothing to animate here.
                if (target.Node is not { } node)
                {
                    continue;
                }

                if (node < 0 || node >= nodes.Length)
                {
                    throw new InvalidDataException($"{name}: node {node} is out of range.");
                }

                AnimationPath path = target.Path switch
                {
                    "translation" => AnimationPath.Translation,
                    "rotation" => AnimationPath.Rotation,
                    "scale" => AnimationPath.Scale,
                    "weights" => throw new NotSupportedException($"{name}: morph target weights are not supported."),
                    _ => throw new InvalidDataException($"{name}: path '{target.Path}' is not translation, rotation, scale or weights.")
                };

                if (nodes[node].Matrix is not null)
                {
                    throw new InvalidDataException($"{name}: node {node} has a matrix, which cannot be animated.");
                }

                if (!targets.Add((node, path)))
                {
                    throw new InvalidDataException($"{name}: another channel of the animation already targets the {target.Path} of node {node}.");
                }

                int samplerIndex = channel.Sampler ?? throw new InvalidDataException($"{name}: sampler is missing.");
                if (samplerIndex < 0 || samplerIndex >= samplers.Length)
                {
                    throw new InvalidDataException($"{name}: sampler {samplerIndex} is out of range.");
                }

                channels.Add(ReadChannel(samplers[samplerIndex], $"{animationName}.samplers[{samplerIndex}]", node, path, accessors));
            }

            animations.Add(new ModelAnimation(animation.Name, channels.ToImmutable()));
        }

        return animations.MoveToImmutable();
    }

    private static AnimationChannel ReadChannel(GltfAnimationSamplerDto sampler, string name, int node, AnimationPath path, AccessorReader accessors)
    {
        AnimationInterpolation interpolation = sampler.Interpolation switch
        {
            null or "LINEAR" => AnimationInterpolation.Linear,
            "STEP" => AnimationInterpolation.Step,
            "CUBICSPLINE" => AnimationInterpolation.CubicSpline,
            _ => throw new InvalidDataException($"{name}: interpolation '{sampler.Interpolation}' is not LINEAR, STEP or CUBICSPLINE.")
        };

        int input = sampler.Input ?? throw new InvalidDataException($"{name}: input is missing.");
        int output = sampler.Output ?? throw new InvalidDataException($"{name}: output is missing.");

        float[] times = accessors.ReadFloats(input, AccessorUse.AnimationInput);
        if (times[0] < 0)
        {
            throw new InvalidDataException($"{name}: the first keyframe time {times[0]} is negative.");
        }

        for (int i = 1; i < times.Length; i++)
        {
            if (times[i] <= times[i - 1])
            {
                throw new InvalidDataException($"{name}: keyframe times are not strictly increasing at keyframe {i}.");
            }
        }

        bool cubic = interpolation == AnimationInterpolation.CubicSpline;
        if (cubic && times.Length < 2)
        {
            throw new InvalidDataException($"{name}: CUBICSPLINE needs at least 2 keyframes.");
        }

        AccessorUse use = path == AnimationPath.Rotation ? AccessorUse.RotationOutput : AccessorUse.TranslationOrScaleOutput;
        int componentCount = path == AnimationPath.Rotation ? 4 : 3;
        float[] values = accessors.ReadFloats(output, use);

        int expected = times.Length * (cubic ? 3 : 1);
        if (values.Length / componentCount != expected)
        {
            throw new InvalidDataException($"{name}: output has {values.Length / componentCount} elements, but {times.Length} keyframes need {expected}.");
        }

        if (path == AnimationPath.Rotation)
        {
            // Key values are unit quaternions, repaired if an exporter left them slightly off. CUBICSPLINE tangents, the first and
            // third element of each keyframe, are derivatives and are never normalized.
            for (int element = 0; element < expected; element++)
            {
                if (cubic && element % 3 != 1)
                {
                    continue;
                }

                int offset = element * 4;
                Quaternion rotation = NormalizeRotation(new Vector4(values[offset], values[offset + 1], values[offset + 2], values[offset + 3]), name);
                values[offset] = rotation.X;
                values[offset + 1] = rotation.Y;
                values[offset + 2] = rotation.Z;
                values[offset + 3] = rotation.W;
            }
        }

        return new AnimationChannel(node, path, interpolation, times, values);
    }

    private static Quaternion NormalizeRotation(Vector4 value, string name)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z) || !float.IsFinite(value.W))
        {
            throw new InvalidDataException($"{name}: a rotation is not finite.");
        }

        float length = value.Length();
        if (length < MinimumLength)
        {
            throw new InvalidDataException($"{name}: a rotation has zero length.");
        }

        Vector4 normalized = value / length;
        return new Quaternion(normalized.X, normalized.Y, normalized.Z, normalized.W);
    }

    private static Vector3 ReadVector3(float[] values, string name, string property)
    {
        if (values.Length != 3 || !values.All(float.IsFinite))
        {
            throw new InvalidDataException($"{name}: {property} must be 3 finite numbers.");
        }

        return new Vector3(values[0], values[1], values[2]);
    }

    // glTF stores matrices column-major for column vectors. System.Numerics uses row vectors, whose matrix is the transpose, so
    // reading the 16 values in order into M11, M12, ..., M44 gives the equivalent matrix, with the translation in M41 to M43.
    internal static Matrix4x4 ReadMatrix(float[] values, int offset, string name, string property)
    {
        if (values.Length - offset < 16)
        {
            throw new InvalidDataException($"{name}: {property} has fewer than 16 values.");
        }

        for (int i = 0; i < 16; i++)
        {
            if (!float.IsFinite(values[offset + i]))
            {
                throw new InvalidDataException($"{name}: {property} has a value that is not finite.");
            }
        }

        return new Matrix4x4(
            values[offset], values[offset + 1], values[offset + 2], values[offset + 3],
            values[offset + 4], values[offset + 5], values[offset + 6], values[offset + 7],
            values[offset + 8], values[offset + 9], values[offset + 10], values[offset + 11],
            values[offset + 12], values[offset + 13], values[offset + 14], values[offset + 15]);
    }

    private static bool AreClose(Matrix4x4 a, Matrix4x4 b, float tolerance)
    {
        Matrix4x4 difference = a - b;
        float scale = 1f;
        for (int row = 0; row < 4; row++)
        {
            for (int column = 0; column < 4; column++)
            {
                scale = MathF.Max(scale, MathF.Abs(b[row, column]));
            }
        }

        for (int row = 0; row < 4; row++)
        {
            for (int column = 0; column < 4; column++)
            {
                if (MathF.Abs(difference[row, column]) > tolerance * scale)
                {
                    return false;
                }
            }
        }

        return true;
    }
}
