using System.Collections.Immutable;
using System.Numerics;
using Pixely.Gpu;

namespace Pixely.Models;

/// <summary>
/// One instance of a shared <see cref="Model"/>: its pose, the world matrix of every node, and the joint matrices of every skin
/// with their storage buffers. Create it with <see cref="ModelInstanceFactory.Create"/>.
/// </summary>
/// <remarks>The instance owns its joint buffers, not the model; dispose the model separately once no instance uses it.</remarks>
public sealed class ModelInstance : IDisposable
{
    private readonly GpuMemorySystem _gpuMemorySystem;
    private readonly Matrix4x4[] _worldMatrices;
    private readonly Matrix4x4[][] _jointMatrices;
    private readonly ImmutableArray<GpuStorageBuffer<Matrix4x4>> _jointBuffers;
    private bool _disposed;

    internal ModelInstance(Model model, ModelPose pose, Matrix4x4[] worldMatrices, Matrix4x4[][] jointMatrices,
        ImmutableArray<GpuStorageBuffer<Matrix4x4>> jointBuffers, GpuMemorySystem gpuMemorySystem)
    {
        Model = model;
        Pose = pose;
        _worldMatrices = worldMatrices;
        _jointMatrices = jointMatrices;
        _jointBuffers = jointBuffers;
        _gpuMemorySystem = gpuMemorySystem;
    }

    public Model Model { get; }

    /// <summary>The local transforms the matrices are computed from. Edit it, then call <see cref="UpdateMatrices"/>.</summary>
    public ModelPose Pose { get; }

    /// <summary>Each node's world matrix, indexed like <see cref="Model.Nodes"/>.</summary>
    public ReadOnlySpan<Matrix4x4> WorldMatrices => _worldMatrices;

    /// <summary>The joint matrices of a skin, in the order of <see cref="ModelSkin.Joints"/>.</summary>
    public ReadOnlySpan<Matrix4x4> GetJointMatrices(int skin) => _jointMatrices[skin];

    /// <summary>The storage buffer holding <see cref="GetJointMatrices"/> for a skinned shader.</summary>
    /// <exception cref="ObjectDisposedException">The instance is disposed.</exception>
    public GpuStorageBuffer<Matrix4x4> GetJointBuffer(int skin)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _jointBuffers[skin];
    }

    /// <summary>Poses the instance at <paramref name="elapsed"/> into an animation that loops over its duration, then updates the matrices.</summary>
    /// <exception cref="ArgumentException">The animation belongs to another model.</exception>
    /// <exception cref="ObjectDisposedException">The instance is disposed.</exception>
    public void Animate(ModelAnimation animation, TimeSpan elapsed)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        // Checked before Reset, so a rejected animation leaves the pose as it was, matching the matrices.
        if (animation.Nodes != Model.Nodes)
        {
            throw new ArgumentException("The animation belongs to another model than the instance.", nameof(animation));
        }

        Pose.Reset();
        Pose.Apply(animation, GetLoopTime(elapsed, animation.Duration));
        UpdateMatrices();
    }

    /// <summary>Computes the world and joint matrices from <see cref="Pose"/> and uploads the joint matrices.</summary>
    /// <exception cref="ObjectDisposedException">The instance is disposed.</exception>
    public void UpdateMatrices()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Pose.ComputeWorldMatrices(_worldMatrices);
        for (int skin = 0; skin < _jointMatrices.Length; skin++)
        {
            ModelPose.ComputeJointMatrices(Model.Skins[skin], _worldMatrices, _jointMatrices[skin]);
            _gpuMemorySystem.UpdateStorageBuffer<Matrix4x4>(_jointBuffers[skin], _jointMatrices[skin]);
        }
    }

    /// <summary>The winding of a skinned draw whose joint matrices are multiplied by <paramref name="placement"/>.</summary>
    /// <remarks>
    /// A skinned mesh has no single world matrix, so its winding follows the signs of its joint matrices' determinants. Blending
    /// joints that turn very differently can still flip a triangle when every sign agrees, which a typical rig does not do.
    /// </remarks>
    public Winding GetSkinWinding(int skin, Matrix4x4 placement) => GetWinding(_jointMatrices[skin], placement);

    /// <summary>The winding of a draw with one world matrix.</summary>
    public static Winding GetWinding(Matrix4x4 world) => GetWinding(world.GetDeterminant());

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (GpuStorageBuffer<Matrix4x4> buffer in _jointBuffers)
        {
            buffer.Dispose();
        }
    }

    // The modulo runs in double, because a float holds a long elapsed time too coarsely to find the position within one loop. An
    // animation whose keyframes are all at time 0 has no duration to loop over.
    internal static float GetLoopTime(TimeSpan elapsed, float duration)
    {
        if (duration <= 0)
        {
            return 0;
        }

        double time = elapsed.TotalSeconds % duration;
        return (float)(time < 0 ? time + duration : time);
    }

    internal static Winding GetWinding(ReadOnlySpan<Matrix4x4> joints, Matrix4x4 placement)
    {
        float placementDeterminant = placement.GetDeterminant();
        Winding? common = null;
        foreach (Matrix4x4 joint in joints)
        {
            // The placement applies after the joint, and the determinant of a product is the product of the determinants.
            Winding winding = GetWinding(joint.GetDeterminant() * placementDeterminant);
            if (winding == Winding.Mixed || (common is not null && winding != common))
            {
                return Winding.Mixed;
            }

            common = winding;
        }

        return common ?? Winding.Mixed;
    }

    // A zero determinant collapses the mesh onto a plane or less, where faces have no winding to cull by. glTF allows it, and
    // animations scale nodes to zero to hide them.
    private static Winding GetWinding(float determinant) => determinant > 0 ? Winding.CounterClockwise : determinant < 0 ? Winding.Clockwise : Winding.Mixed;
}
