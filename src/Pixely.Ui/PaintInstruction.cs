using System.Numerics;
using Pixely.Gpu;

namespace Pixely.Ui;

/// <summary>
/// One textured quad. Emission order is paint order, so there is no depth to reconstruct.
/// A null <see cref="Texture"/> means a solid fill: the renderer substitutes a 1x1 white texture,
/// which keeps the whole UI on a single pipeline and keeps this type free of GPU resources.
/// </summary>
internal readonly record struct PaintInstruction(
    Rectangle Area,
    Rectangle Clip,
    Texture? Texture,
    Vector4 Uvs,
    FColor Tint)
{
    internal static readonly Vector4 FullUvs = new(0f, 0f, 1f, 1f);
}
