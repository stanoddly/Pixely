using System.Collections.Immutable;
using System.Numerics;

namespace Pixely.Models;

public enum AnimationPath
{
    Translation,
    Rotation,
    Scale
}

public enum AnimationInterpolation
{
    Step,
    Linear,
    CubicSpline
}

/// <summary>A keyframe animation of node transforms. Play it with <see cref="ModelPose.Apply"/>.</summary>
public sealed class ModelAnimation
{
    internal ModelAnimation(string? name, ImmutableArray<AnimationChannel> channels, ImmutableArray<ModelNode> nodes)
    {
        Name = name;
        Channels = channels;
        Nodes = nodes;
        Duration = channels.IsEmpty ? 0f : channels.Max(channel => channel.Times[^1]);
    }

    public string? Name { get; }

    /// <summary>The last keyframe time over all channels, in seconds. 0 when every channel has a single keyframe at time 0.</summary>
    public float Duration { get; }

    public ImmutableArray<AnimationChannel> Channels { get; }

    // The nodes of the model the animation belongs to, so a pose can reject an animation of another model, whose channels index
    // other nodes.
    internal ImmutableArray<ModelNode> Nodes { get; }
}

/// <summary>Animates one transform component of one node.</summary>
public sealed class AnimationChannel
{
    // CUBICSPLINE stores an in-tangent, a value and an out-tangent per keyframe, in that order; the other interpolations store the
    // value alone. Rotation values are unit quaternions, tangents are not.
    private readonly float[] _values;
    private readonly int _componentCount;

    internal AnimationChannel(int node, AnimationPath path, AnimationInterpolation interpolation, float[] times, float[] values)
    {
        Node = node;
        Path = path;
        Interpolation = interpolation;
        Times = times;
        _values = values;
        _componentCount = path == AnimationPath.Rotation ? 4 : 3;
    }

    public int Node { get; }
    public AnimationPath Path { get; }
    public AnimationInterpolation Interpolation { get; }

    internal float[] Times { get; }

    internal Vector4 Sample(float time)
    {
        float[] times = Times;
        int last = times.Length - 1;

        if (time <= times[0])
        {
            return GetValue(0);
        }

        if (time >= times[last])
        {
            return GetValue(last);
        }

        // The keyframe at or before the time; times are strictly increasing, so there is always a next one here.
        int found = Array.BinarySearch(times, time);
        int previous = found >= 0 ? found : ~found - 1;
        int next = previous + 1;

        float delta = times[next] - times[previous];
        float u = (time - times[previous]) / delta;

        switch (Interpolation)
        {
            case AnimationInterpolation.Step:
            {
                return GetValue(previous);
            }
            case AnimationInterpolation.Linear:
            {
                Vector4 a = GetValue(previous);
                Vector4 b = GetValue(next);
                if (Path == AnimationPath.Rotation)
                {
                    Quaternion rotation = Quaternion.Slerp(ToQuaternion(a), ToQuaternion(b), u);
                    return new Vector4(rotation.X, rotation.Y, rotation.Z, rotation.W);
                }

                return Vector4.Lerp(a, b, u);
            }
            default:
            {
                // glTF 2.0, appendix C: cubic Hermite spline with tangents scaled by the keyframe delta.
                Vector4 p0 = GetValue(previous);
                Vector4 m0 = delta * GetElement(previous * 3 + 2);
                Vector4 p1 = GetValue(next);
                Vector4 m1 = delta * GetElement(next * 3);

                float u2 = u * u;
                float u3 = u2 * u;
                Vector4 result = (2 * u3 - 3 * u2 + 1) * p0 + (u3 - 2 * u2 + u) * m0 + (-2 * u3 + 3 * u2) * p1 + (u3 - u2) * m1;

                // Finite keyframes can still give a result with no direction or out of float range: two keys of opposite sign
                // with zero tangents meet at a zero quaternion, and huge tangents overflow. The nearer keyframe stands in then.
                float length = result.Length();
                if (!float.IsFinite(length) || (Path == AnimationPath.Rotation && length < 1e-6f))
                {
                    return u < 0.5f ? p0 : p1;
                }

                return Path == AnimationPath.Rotation ? result / length : result;
            }
        }
    }

    private Vector4 GetValue(int keyframe) => GetElement(Interpolation == AnimationInterpolation.CubicSpline ? keyframe * 3 + 1 : keyframe);

    private Vector4 GetElement(int element)
    {
        int offset = element * _componentCount;
        return _componentCount == 4
            ? new Vector4(_values[offset], _values[offset + 1], _values[offset + 2], _values[offset + 3])
            : new Vector4(_values[offset], _values[offset + 1], _values[offset + 2], 0f);
    }

    internal static Quaternion ToQuaternion(Vector4 value) => new(value.X, value.Y, value.Z, value.W);
}
