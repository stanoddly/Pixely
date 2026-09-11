namespace Pixely.Ui;

/// <summary>
/// What a renderer is allowed to see of a <see cref="UiRoot"/>: the completed instructions, and the
/// facts needed to decide whether they are still current. Deliberately without <c>Update</c> and
/// <c>SetViewportSize</c>. Building the tree raises application callbacks, and a renderer may not
/// raise those: the renderers sharing a frame are entitled to domain data that does not change
/// underneath them. Same-assembly code can still cast back to <see cref="UiRoot"/>; the point is
/// that no ordinary edit reaches a build by accident.
/// </summary>
internal interface IUiPaintSource
{
    IReadOnlyList<PaintInstruction> Instructions { get; }

    IReadOnlyList<PaintBatch> Batches { get; }

    Vector2Int PaintedViewportSize { get; }

    Vector2Int ViewportSize { get; }

    ulong PaintVersion { get; }
}
