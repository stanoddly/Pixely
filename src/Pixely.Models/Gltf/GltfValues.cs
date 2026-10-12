using System.Numerics;

namespace Pixely.Models.Gltf;

// Checks and repairs of values that more than one part of the file uses.
internal static class GltfValues
{
    public const float MinimumLength = 1e-12f;

    public static Quaternion NormalizeRotation(Vector4 value, string name)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z) || !float.IsFinite(value.W))
        {
            throw new InvalidDataException($"{name}: a rotation is not finite.");
        }

        Vector4 normalized = Normalize(value, name, "a rotation");
        return new Quaternion(normalized.X, normalized.Y, normalized.Z, normalized.W);
    }

    // The length is a double, so finite components as large as float.MaxValue cannot overflow it to infinity.
    public static Vector4 Normalize(Vector4 value, string name, string what)
    {
        double length = Math.Sqrt((double)value.X * value.X + (double)value.Y * value.Y + (double)value.Z * value.Z + (double)value.W * value.W);
        if (length < MinimumLength)
        {
            throw new InvalidDataException($"{name}: {what} has zero length.");
        }

        return new Vector4((float)(value.X / length), (float)(value.Y / length), (float)(value.Z / length), (float)(value.W / length));
    }

    // glTF stores matrices column-major for column vectors. System.Numerics uses row vectors, whose matrix is the transpose, so
    // reading the 16 values in order into M11, M12, ..., M44 gives the equivalent matrix, with the translation in M41 to M43.
    public static Matrix4x4 ReadMatrix(float[] values, int offset, string name, string property)
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
}
