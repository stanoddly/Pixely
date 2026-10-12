using System.Collections.Immutable;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Pixely.Models.Gltf;

// Reads skins: their joints and inverse bind matrices.
internal static class GltfSkinReader
{
    private const float AffineTolerance = 1e-5f;

    public static ImmutableArray<ModelSkin> ReadSkins(GltfSkinDto[] skinDtos, int nodeCount, AccessorReader accessors)
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

            // Each joint index takes a few bytes of JSON and a 64-byte matrix here, so skins that repeat joints are charged too.
            accessors.Charge((long)joints.Length * Unsafe.SizeOf<Matrix4x4>(), name);
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
                    Matrix4x4 matrix = GltfValues.ReadMatrix(values, joint * 16, name, $"inverseBindMatrices[{joint}]");
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
}
