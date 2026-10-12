using System.Collections.Immutable;
using System.Numerics;
using Pixely.Gpu;

namespace Pixely.Models;

/// <summary>Creates <see cref="ModelInstance"/>s, each with its own pose and joint buffers. Registered by <see cref="ModelLoadingExtensions.RegisterModelLoading"/>.</summary>
public sealed class ModelInstanceFactory
{
    private readonly GpuMemorySystem _gpuMemorySystem;

    public ModelInstanceFactory(GpuMemorySystem gpuMemorySystem)
    {
        _gpuMemorySystem = gpuMemorySystem;
    }

    /// <summary>Creates an instance in the pose the file gives the model, with its matrices computed and uploaded. The caller disposes it.</summary>
    public ModelInstance Create(Model model)
    {
        ModelPose pose = new(model);
        Matrix4x4[] worldMatrices = new Matrix4x4[model.Nodes.Length];
        pose.ComputeWorldMatrices(worldMatrices);

        Matrix4x4[][] jointMatrices = new Matrix4x4[model.Skins.Length][];
        ImmutableArray<GpuStorageBuffer<Matrix4x4>>.Builder jointBuffers = ImmutableArray.CreateBuilder<GpuStorageBuffer<Matrix4x4>>(model.Skins.Length);
        try
        {
            for (int skin = 0; skin < model.Skins.Length; skin++)
            {
                jointMatrices[skin] = new Matrix4x4[model.Skins[skin].Joints.Length];
                ModelPose.ComputeJointMatrices(model.Skins[skin], worldMatrices, jointMatrices[skin]);
                jointBuffers.Add(_gpuMemorySystem.CreateStorageBuffer<Matrix4x4>(jointMatrices[skin]));
            }
        }
        catch
        {
            ResourceScope.DisposeInReverse(jointBuffers);
            throw;
        }

        return new ModelInstance(model, pose, worldMatrices, jointMatrices, jointBuffers.MoveToImmutable(), _gpuMemorySystem);
    }
}
