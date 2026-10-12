using System.Collections.Immutable;

namespace Pixely.Models;

/// <summary>A node of the model's hierarchy. <see cref="Mesh"/>, <see cref="Skin"/>, <see cref="Parent"/> and <see cref="Children"/> index the model's arrays.</summary>
public sealed record ModelNode(string? Name, int? Parent, ImmutableArray<int> Children, NodeTransform LocalTransform, int? Mesh, int? Skin);

/// <summary>A scene: the root nodes drawn together, and every node with a mesh under them, the nodes a renderer draws.</summary>
public sealed record ModelScene(string? Name, ImmutableArray<int> RootNodes, ImmutableArray<int> MeshNodes);
