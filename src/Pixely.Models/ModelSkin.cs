using System.Collections.Immutable;
using System.Numerics;

namespace Pixely.Models;

/// <summary>A skeleton: the joint nodes, in the order <see cref="SkinVertex"/> indexes them, and their inverse bind matrices.</summary>
public sealed record ModelSkin(string? Name, ImmutableArray<int> Joints, ImmutableArray<Matrix4x4> InverseBindMatrices, int? Skeleton);
