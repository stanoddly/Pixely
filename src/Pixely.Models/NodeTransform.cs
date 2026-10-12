using System.Numerics;

namespace Pixely.Models;

/// <summary>A node's transform relative to its parent: scale, then rotation, then translation.</summary>
public readonly record struct NodeTransform(Vector3 Translation, Quaternion Rotation, Vector3 Scale)
{
    public static NodeTransform Identity { get; } = new(Vector3.Zero, Quaternion.Identity, Vector3.One);

    /// <summary>The transform as a row-vector matrix, so a point transforms as <c>Vector3.Transform(point, matrix)</c>.</summary>
    public Matrix4x4 ToMatrix() => Matrix4x4.CreateScale(Scale) * Matrix4x4.CreateFromQuaternion(Rotation) * Matrix4x4.CreateTranslation(Translation);
}
