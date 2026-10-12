using System.Collections.Immutable;
using System.Numerics;
using System.Runtime.CompilerServices;
using Pixely.Gpu;

namespace Pixely.Models.Gltf;

// Reads meshes into the vertex, skin and index arrays the loader uploads.
internal static class GltfMeshReader
{
    private const int TrianglesMode = 4;

    public static ImmutableArray<MeshData> ReadMeshes(GltfRootDto root, AccessorReader accessors)
    {
        GltfMeshDto[] meshDtos = root.Meshes ?? [];
        int materialCount = root.Materials?.Length ?? 0;
        SharedArrays shared = new();
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
                primitives.Add(ReadPrimitive(primitiveDtos[primitiveIndex], name, materialCount, accessors, shared));
            }

            meshes.Add(new MeshData(meshDtos[meshIndex].Name, primitives.MoveToImmutable()));
        }

        return meshes.MoveToImmutable();
    }

    private static PrimitiveData ReadPrimitive(GltfPrimitiveDto primitive, string name, int materialCount, AccessorReader accessors, SharedArrays shared)
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

        bool hasTexCoords = attributes.TryGetValue("TEXCOORD_0", out int texCoordAccessor);
        (int Position, int Normal, int? TexCoord) vertexKey = (positionAccessor, normalAccessor, hasTexCoords ? texCoordAccessor : null);
        if (!shared.Vertices.TryGetValue(vertexKey, out PositionTextureNormalVertex[]? vertices))
        {
            vertices = ReadVertices(name, positionAccessor, normalAccessor, vertexKey.TexCoord, accessors);
            shared.Vertices.Add(vertexKey, vertices);
        }

        int vertexCount = vertices.Length;

        SkinVertex[]? skin = null;
        int maxJoint = -1;
        if (hasJoints)
        {
            if (!shared.Skins.TryGetValue((jointsAccessor, weightsAccessor), out (SkinVertex[] Skin, int MaxJoint) skinData))
            {
                skinData = ReadSkin(name, jointsAccessor, weightsAccessor, accessors);
                shared.Skins.Add((jointsAccessor, weightsAccessor), skinData);
            }

            CheckCount(name, "JOINTS_0 and WEIGHTS_0", skinData.Skin.Length, vertexCount);
            (skin, maxJoint) = skinData;
        }

        IndexArrays indices;
        if (primitive.Indices is { } indexAccessor)
        {
            if (!shared.Indices.TryGetValue(indexAccessor, out (IndexArrays Indices, uint MaxIndex) indexData))
            {
                indexData = ReadIndices(name, indexAccessor, accessors);
                shared.Indices.Add(indexAccessor, indexData);
            }

            if (indexData.MaxIndex >= vertexCount)
            {
                throw new InvalidDataException($"{name}: index {indexData.MaxIndex} is out of range for {vertexCount} vertices.");
            }

            indices = indexData.Indices;
        }
        else
        {
            if (vertexCount % 3 != 0)
            {
                throw new InvalidDataException($"{name}: {vertexCount} vertices without indices do not form whole triangles.");
            }

            if (!shared.SequentialIndices.TryGetValue(vertexCount, out indices))
            {
                indices = CreateSequentialIndices(vertexCount, name, accessors);
                shared.SequentialIndices.Add(vertexCount, indices);
            }
        }

        return new PrimitiveData(vertices, skin, indices.Indices16, indices.Indices32, primitive.Material, maxJoint);
    }

    private static PositionTextureNormalVertex[] ReadVertices(string name, int positionAccessor, int normalAccessor, int? texCoordAccessor, AccessorReader accessors)
    {
        float[] positions = accessors.ReadFloats(positionAccessor, AccessorUse.Position);
        int vertexCount = positions.Length / 3;

        float[] normals = accessors.ReadFloats(normalAccessor, AccessorUse.Normal);
        CheckCount(name, "NORMAL", normals.Length / 3, vertexCount);

        float[]? texCoords = null;
        if (texCoordAccessor is { } accessor)
        {
            texCoords = accessors.ReadFloats(accessor, AccessorUse.TexCoord);
            CheckCount(name, "TEXCOORD_0", texCoords.Length / 2, vertexCount);
        }

        accessors.Charge((long)vertexCount * Unsafe.SizeOf<PositionTextureNormalVertex>(), name);
        PositionTextureNormalVertex[] vertices = new PositionTextureNormalVertex[vertexCount];
        for (int vertex = 0; vertex < vertexCount; vertex++)
        {
            Vector3 position = new(positions[vertex * 3], positions[vertex * 3 + 1], positions[vertex * 3 + 2]);
            Vector3 normal = new(normals[vertex * 3], normals[vertex * 3 + 1], normals[vertex * 3 + 2]);
            // Exporters often write normals that are not quite unit length, so they are repaired instead of rejected.
            Vector4 unitNormal = GltfValues.Normalize(new Vector4(normal, 0), name, $"the normal of vertex {vertex}");
            Vector2 texCoord = texCoords is null ? Vector2.Zero : new Vector2(texCoords[vertex * 2], texCoords[vertex * 2 + 1]);
            vertices[vertex] = new PositionTextureNormalVertex(position, texCoord, new Vector3(unitNormal.X, unitNormal.Y, unitNormal.Z));
        }

        return vertices;
    }

    private static (SkinVertex[] Skin, int MaxJoint) ReadSkin(string name, int jointsAccessor, int weightsAccessor, AccessorReader accessors)
    {
        uint[] joints = accessors.ReadIntegers(jointsAccessor, AccessorUse.Joints).Values;
        float[] weights = accessors.ReadFloats(weightsAccessor, AccessorUse.Weights);
        int vertexCount = joints.Length / 4;
        CheckCount(name, "WEIGHTS_0", weights.Length / 4, vertexCount);

        accessors.Charge((long)vertexCount * Unsafe.SizeOf<SkinVertex>(), name);
        SkinVertex[] skin = new SkinVertex[vertexCount];
        int maxJoint = -1;
        for (int vertex = 0; vertex < vertexCount; vertex++)
        {
            Vector4 weight = new(weights[vertex * 4], weights[vertex * 4 + 1], weights[vertex * 4 + 2], weights[vertex * 4 + 3]);
            if (weight.X < 0 || weight.Y < 0 || weight.Z < 0 || weight.W < 0)
            {
                throw new InvalidDataException($"{name}: vertex {vertex} has a negative weight.");
            }

            // Quantized weights rarely sum to exactly 1, and exporters often miss it, so the sum is repaired instead of checked. The
            // sum is a double, so four large finite weights cannot overflow it.
            // A joint repeated within a vertex is kept: the blend adds its weights, which equals one combined weight.
            double sum = (double)weight.X + weight.Y + weight.Z + weight.W;
            if (sum < GltfValues.MinimumLength)
            {
                throw new InvalidDataException($"{name}: the weights of vertex {vertex} sum to 0.");
            }

            ushort joint0 = (ushort)joints[vertex * 4];
            ushort joint1 = (ushort)joints[vertex * 4 + 1];
            ushort joint2 = (ushort)joints[vertex * 4 + 2];
            ushort joint3 = (ushort)joints[vertex * 4 + 3];
            maxJoint = Math.Max(maxJoint, Math.Max(Math.Max(joint0, joint1), Math.Max(joint2, joint3)));
            Vector4 normalized = new((float)(weight.X / sum), (float)(weight.Y / sum), (float)(weight.Z / sum), (float)(weight.W / sum));
            skin[vertex] = new SkinVertex(joint0, joint1, joint2, joint3, normalized);
        }

        return (skin, maxJoint);
    }

    private static (IndexArrays Indices, uint MaxIndex) ReadIndices(string name, int indexAccessor, AccessorReader accessors)
    {
        IntegerAccessorData indexData = accessors.ReadIntegers(indexAccessor, AccessorUse.Indices);
        uint[] indices = indexData.Values;
        if (indices.Length % 3 != 0)
        {
            throw new InvalidDataException($"{name}: {indices.Length} indices do not form whole triangles.");
        }

        bool wide = indexData.ComponentType == AccessorReader.UnsignedInt;
        uint maxIndex = 0;
        foreach (uint index in indices)
        {
            // glTF forbids the largest value of the index type, because graphics APIs can read it as a primitive restart. Byte
            // indices are uploaded as 16 bits, where 255 is an ordinary index, and a 32-bit one is always past the vertex count.
            if (!wide && index == ushort.MaxValue)
            {
                throw new InvalidDataException($"{name}: index {index} is the primitive restart value of 16-bit indices.");
            }

            maxIndex = Math.Max(maxIndex, index);
        }

        if (wide)
        {
            return (new IndexArrays(null, indices), maxIndex);
        }

        accessors.Charge(indices.Length * (long)sizeof(ushort), name);
        return (new IndexArrays(Array.ConvertAll(indices, index => (ushort)index), null), maxIndex);
    }

    private static IndexArrays CreateSequentialIndices(int vertexCount, string name, AccessorReader accessors)
    {
        // 16-bit indices address 65,535 vertices, 0 to 65,534; 65,535 is the primitive restart value.
        bool narrow = vertexCount <= ushort.MaxValue;
        accessors.Charge((long)vertexCount * (narrow ? sizeof(ushort) : sizeof(uint)), name);
        if (narrow)
        {
            ushort[] indices16 = new ushort[vertexCount];
            for (int i = 0; i < vertexCount; i++)
            {
                indices16[i] = (ushort)i;
            }

            return new IndexArrays(indices16, null);
        }

        uint[] indices32 = new uint[vertexCount];
        for (int i = 0; i < vertexCount; i++)
        {
            indices32[i] = (uint)i;
        }

        return new IndexArrays(null, indices32);
    }

    private static void CheckCount(string name, string attribute, int count, int vertexCount)
    {
        if (count != vertexCount)
        {
            throw new InvalidDataException($"{name}: {attribute} has {count} elements, but POSITION has {vertexCount}.");
        }
    }

    // Primitives that read the same accessors share the converted arrays, so the loader uploads each array once.
    private sealed class SharedArrays
    {
        public Dictionary<(int Position, int Normal, int? TexCoord), PositionTextureNormalVertex[]> Vertices { get; } = new();
        public Dictionary<(int Joints, int Weights), (SkinVertex[] Skin, int MaxJoint)> Skins { get; } = new();
        public Dictionary<int, (IndexArrays Indices, uint MaxIndex)> Indices { get; } = new();
        public Dictionary<int, IndexArrays> SequentialIndices { get; } = new();
    }

    // Exactly one of the two is set.
    private readonly record struct IndexArrays(ushort[]? Indices16, uint[]? Indices32);
}
