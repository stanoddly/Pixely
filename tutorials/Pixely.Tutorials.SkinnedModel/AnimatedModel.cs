using System.Collections.Immutable;
using System.Numerics;
using Pixely.Gpu;
using Pixely.Models;

namespace Pixely.Tutorials.SkinnedModel;

// How a draw decides its front face. A skinned mesh has no single world matrix, so its winding follows the signs of its joint
// matrices' determinants; when they differ, triangles wind both ways and no face can be culled. Blending very different joints can
// still flip a triangle when every sign agrees, which a typical rig does not do.
public enum Winding
{
    CounterClockwise,
    Clockwise,
    Mixed
}

// One copy of the model on screen: where it stands, and whether it is drawn as if its materials were double-sided.
public sealed record ModelCopy(string Name, Matrix4x4 Placement, bool ForceDoubleSided);

// Plays the model's first animation and keeps the matrices each copy draws with. The copies share one pose, since they show the
// same model at the same time, and differ only in their placement.
public sealed class AnimatedModel : IUpdatable, IDisposable
{
    private readonly FrameClock _frameClock;
    private readonly GpuMemorySystem _gpuMemorySystem;
    private readonly ModelPose _pose;
    private readonly Matrix4x4[] _world;
    private readonly Matrix4x4[] _jointScratch;
    private readonly CopyState[] _copies;

    private AnimatedModel(Model model, ImmutableArray<ModelCopy> copies, FrameClock frameClock, GpuMemorySystem gpuMemorySystem)
    {
        Model = model;
        Copies = copies;
        _frameClock = frameClock;
        _gpuMemorySystem = gpuMemorySystem;
        _pose = new ModelPose(model);
        _world = new Matrix4x4[model.Nodes.Length];
        _jointScratch = new Matrix4x4[model.Skins.IsEmpty ? 0 : model.Skins.Max(skin => skin.Joints.Length)];
        MeshNodes = CollectMeshNodes(model);

        _copies = new CopyState[copies.Length];
        // Nothing owns the buffers until the constructor returns, so a failure releases the ones created so far.
        List<GpuStorageBuffer<Matrix4x4>> created = new();
        try
        {
            for (int copy = 0; copy < copies.Length; copy++)
            {
                GpuStorageBuffer<Matrix4x4>[] jointBuffers = new GpuStorageBuffer<Matrix4x4>[model.Skins.Length];
                for (int skin = 0; skin < model.Skins.Length; skin++)
                {
                    jointBuffers[skin] = gpuMemorySystem.CreateStorageBuffer<Matrix4x4>(new Matrix4x4[model.Skins[skin].Joints.Length]);
                    created.Add(jointBuffers[skin]);
                }

                _copies[copy] = new CopyState(jointBuffers, new Winding[model.Skins.Length]);
            }

            Update();
        }
        catch
        {
            created.ForEach(buffer => buffer.Dispose());
            throw;
        }
    }

    public Model Model { get; }
    public ImmutableArray<ModelCopy> Copies { get; }

    // The nodes with a mesh in the scene being drawn: the default scene, or the first one when the file names none.
    public ImmutableArray<int> MeshNodes { get; }

    public ReadOnlySpan<Matrix4x4> World => _world;

    public GpuStorageBuffer<Matrix4x4> GetJointBuffer(int copy, int skin) => _copies[copy].JointBuffers[skin];

    public Winding GetWinding(int copy, int skin) => _copies[copy].Windings[skin];

    public void Update()
    {
        _pose.Reset();
        if (!Model.Animations.IsEmpty)
        {
            ModelAnimation animation = Model.Animations[0];
            float elapsed = (float)_frameClock.ElapsedTime.TotalSeconds;
            // A single-keyframe animation has no duration to loop over.
            float time = animation.Duration > 0 ? elapsed % animation.Duration : 0;
            _pose.Apply(animation, time);
        }

        _pose.ComputeWorldMatrices(_world);

        for (int copy = 0; copy < _copies.Length; copy++)
        {
            for (int skin = 0; skin < Model.Skins.Length; skin++)
            {
                Span<Matrix4x4> joints = _jointScratch.AsSpan(0, Model.Skins[skin].Joints.Length);
                ModelPose.ComputeJointMatrices(Model.Skins[skin], _world, joints);

                int positive = 0;
                for (int i = 0; i < joints.Length; i++)
                {
                    // The placement applies after the skin, so it moves or mirrors the whole skinned mesh.
                    joints[i] *= Copies[copy].Placement;
                    positive += joints[i].GetDeterminant() > 0 ? 1 : 0;
                }

                _copies[copy].Windings[skin] = positive == joints.Length ? Winding.CounterClockwise : positive == 0 ? Winding.Clockwise : Winding.Mixed;
                _gpuMemorySystem.UpdateStorageBuffer<Matrix4x4>(_copies[copy].JointBuffers[skin], joints);
            }
        }
    }

    public void Dispose()
    {
        foreach (CopyState copy in _copies)
        {
            foreach (GpuStorageBuffer<Matrix4x4> buffer in copy.JointBuffers)
            {
                buffer.Dispose();
            }
        }

        Model.Dispose();
    }

    public static AnimatedModel Create(IModelLoader modelLoader, FrameClock frameClock, GpuMemorySystem gpuMemorySystem)
    {
        Model model = modelLoader.Load("models/CesiumMan.glb");

        // Left: mirrored on X, so its joint determinants are negative and it draws with clockwise front faces.
        // Middle: as loaded. Right: drawn as if double-sided, with no culling.
        ImmutableArray<ModelCopy> copies =
        [
            new ModelCopy("mirrored", Matrix4x4.CreateScale(-1, 1, 1) * Matrix4x4.CreateTranslation(-1.4f, 0, 0), ForceDoubleSided: false),
            new ModelCopy("as loaded", Matrix4x4.Identity, ForceDoubleSided: false),
            new ModelCopy("double-sided", Matrix4x4.CreateTranslation(1.4f, 0, 0), ForceDoubleSided: true)
        ];

        try
        {
            return new AnimatedModel(model, copies, frameClock, gpuMemorySystem);
        }
        catch
        {
            model.Dispose();
            throw;
        }
    }

    private static ImmutableArray<int> CollectMeshNodes(Model model)
    {
        ImmutableArray<int> roots = model.Scenes.IsEmpty ? [] : model.Scenes[model.DefaultScene ?? 0].RootNodes;
        ImmutableArray<int>.Builder meshNodes = ImmutableArray.CreateBuilder<int>();
        Stack<int> pending = new(roots);
        while (pending.Count > 0)
        {
            int node = pending.Pop();
            if (model.Nodes[node].Mesh is not null)
            {
                meshNodes.Add(node);
            }

            foreach (int child in model.Nodes[node].Children)
            {
                pending.Push(child);
            }
        }

        return meshNodes.ToImmutable();
    }

    private sealed record CopyState(GpuStorageBuffer<Matrix4x4>[] JointBuffers, Winding[] Windings);
}
