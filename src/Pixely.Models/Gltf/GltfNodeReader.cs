using System.Collections.Immutable;
using System.Numerics;

namespace Pixely.Models.Gltf;

// Reads the node hierarchy and the scenes, and checks that the hierarchy is a forest.
internal static class GltfNodeReader
{
    private const float MatrixTolerance = 1e-4f;

    public static int[] ReadParents(GltfNodeDto[] nodes)
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
    public static ImmutableArray<int> ReadParentFirstOrder(GltfNodeDto[] nodes, int[] parents)
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

    public static ImmutableArray<ModelNode> ReadNodes(GltfRootDto root, GltfNodeDto[] nodeDtos, int[] parents)
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

            Matrix4x4 matrix = GltfValues.ReadMatrix(matrixValues, 0, name, "matrix");

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

            nodeRotation = GltfValues.NormalizeRotation(new Vector4(r[0], r[1], r[2], r[3]), name);
        }

        return new NodeTransform(nodeTranslation, nodeRotation, nodeScale);
    }

    public static ImmutableArray<ModelScene> ReadScenes(GltfSceneDto[] sceneDtos, ImmutableArray<ModelNode> nodes, int[] parents, AccessorReader accessors)
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

            scenes.Add(new ModelScene(sceneDtos[index].Name, [.. roots], CollectMeshNodes(nodes, roots, $"scenes[{index}]", accessors)));
        }

        return scenes.MoveToImmutable();
    }

    // A depth-first walk from the roots. The hierarchy has been checked for cycles, so the walk ends. Many scenes can share one
    // large hierarchy, each for a few bytes of JSON, so every visit is charged like a converted 4-byte index; it bounds both the
    // walk and the mesh nodes it keeps.
    private static ImmutableArray<int> CollectMeshNodes(ImmutableArray<ModelNode> nodes, int[] roots, string name, AccessorReader accessors)
    {
        ImmutableArray<int>.Builder meshNodes = ImmutableArray.CreateBuilder<int>();
        Stack<int> pending = new(roots);
        while (pending.Count > 0)
        {
            accessors.Charge(sizeof(int), name);
            int node = pending.Pop();
            if (nodes[node].Mesh is not null)
            {
                meshNodes.Add(node);
            }

            foreach (int child in nodes[node].Children)
            {
                pending.Push(child);
            }
        }

        return meshNodes.ToImmutable();
    }

    private static Vector3 ReadVector3(float[] values, string name, string property)
    {
        if (values.Length != 3 || !values.All(float.IsFinite))
        {
            throw new InvalidDataException($"{name}: {property} must be 3 finite numbers.");
        }

        return new Vector3(values[0], values[1], values[2]);
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
