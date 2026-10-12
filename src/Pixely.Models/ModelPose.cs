using System.Collections.Immutable;
using System.Numerics;

namespace Pixely.Models;

/// <summary>
/// The local transforms of one instance of a model. Two characters that share a <see cref="Model"/> use two poses. Nothing here
/// allocates after construction.
/// </summary>
public sealed class ModelPose
{
    private readonly ImmutableArray<ModelNode> _nodes;
    private readonly ImmutableArray<int> _parentFirstOrder;
    private readonly NodeTransform[] _localTransforms;

    public ModelPose(Model model) : this(model.Nodes, model.ParentFirstOrder)
    {
    }

    internal ModelPose(ImmutableArray<ModelNode> nodes, ImmutableArray<int> parentFirstOrder)
    {
        _nodes = nodes;
        _parentFirstOrder = parentFirstOrder;
        _localTransforms = new NodeTransform[nodes.Length];
        Reset();
    }

    /// <summary>One transform per node, indexed like <see cref="Model.Nodes"/>.</summary>
    public Span<NodeTransform> LocalTransforms => _localTransforms;

    /// <summary>Returns every node to the transform the file gives it.</summary>
    public void Reset()
    {
        for (int i = 0; i < _nodes.Length; i++)
        {
            _localTransforms[i] = _nodes[i].LocalTransform;
        }
    }

    /// <summary>Sets the animated components of the animated nodes to their values at <paramref name="time"/> seconds. Times outside the animation clamp to its first or last keyframe; looping is the caller's choice.</summary>
    public void Apply(ModelAnimation animation, float time)
    {
        if (float.IsNaN(time))
        {
            throw new ArgumentException("The time is NaN.", nameof(time));
        }

        foreach (AnimationChannel channel in animation.Channels)
        {
            Vector4 value = channel.Sample(time);
            ref NodeTransform transform = ref _localTransforms[channel.Node];
            transform = channel.Path switch
            {
                AnimationPath.Translation => transform with { Translation = new Vector3(value.X, value.Y, value.Z) },
                AnimationPath.Rotation => transform with { Rotation = AnimationChannel.ToQuaternion(value) },
                _ => transform with { Scale = new Vector3(value.X, value.Y, value.Z) }
            };
        }
    }

    /// <summary>Writes each node's world matrix, <c>local * parentWorld</c>, indexed like <see cref="Model.Nodes"/>.</summary>
    public void ComputeWorldMatrices(Span<Matrix4x4> world)
    {
        if (world.Length < _nodes.Length)
        {
            throw new ArgumentException($"The span holds {world.Length} matrices; the model has {_nodes.Length} nodes.", nameof(world));
        }

        foreach (int node in _parentFirstOrder)
        {
            Matrix4x4 local = _localTransforms[node].ToMatrix();
            world[node] = _nodes[node].Parent is { } parent ? local * world[parent] : local;
        }
    }

    /// <summary>Writes each joint's skinning matrix, <c>inverseBind * world[joint]</c>, in the order of <see cref="ModelSkin.Joints"/>.</summary>
    /// <remarks>The skinned mesh node's own transform is not part of the result, as glTF requires.</remarks>
    public static void ComputeJointMatrices(ModelSkin skin, ReadOnlySpan<Matrix4x4> world, Span<Matrix4x4> joints)
    {
        if (joints.Length < skin.Joints.Length)
        {
            throw new ArgumentException($"The span holds {joints.Length} matrices; the skin has {skin.Joints.Length} joints.", nameof(joints));
        }

        for (int i = 0; i < skin.Joints.Length; i++)
        {
            joints[i] = skin.InverseBindMatrices[i] * world[skin.Joints[i]];
        }
    }
}
