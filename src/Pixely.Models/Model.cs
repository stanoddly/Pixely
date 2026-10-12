using System.Collections.Immutable;

namespace Pixely.Models;

/// <summary>A loaded glTF model: its node hierarchy, scenes, GPU meshes and textures, skins and animations.</summary>
/// <remarks>The model owns its GPU buffers and textures; dispose it to release them. Coordinates are glTF's: right-handed, +Y up, meters.</remarks>
public sealed class Model : IDisposable
{
    private readonly ImmutableArray<IDisposable> _resources;
    private bool _disposed;

    internal Model(
        ImmutableArray<ModelNode> nodes,
        ImmutableArray<int> parentFirstOrder,
        ImmutableArray<ModelScene> scenes,
        int? defaultScene,
        ImmutableArray<ModelMesh> meshes,
        ImmutableArray<ModelMaterial> materials,
        ImmutableArray<ModelSkin> skins,
        ImmutableArray<ModelAnimation> animations,
        ImmutableArray<IDisposable> resources)
    {
        Nodes = nodes;
        ParentFirstOrder = parentFirstOrder;
        Scenes = scenes;
        DefaultScene = defaultScene;
        Meshes = meshes;
        Materials = materials;
        Skins = skins;
        Animations = animations;
        _resources = resources;
    }

    /// <summary>All nodes in file order, so indices match glTF tools.</summary>
    public ImmutableArray<ModelNode> Nodes { get; }

    public ImmutableArray<ModelScene> Scenes { get; }

    /// <summary>The scene the file names as its default, or null when it names none.</summary>
    public int? DefaultScene { get; }

    public ImmutableArray<ModelMesh> Meshes { get; }
    public ImmutableArray<ModelMaterial> Materials { get; }
    public ImmutableArray<ModelSkin> Skins { get; }
    public ImmutableArray<ModelAnimation> Animations { get; }

    // Node indices ordered so that a parent always comes before its children.
    internal ImmutableArray<int> ParentFirstOrder { get; }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ResourceScope.DisposeInReverse(_resources);
    }
}
