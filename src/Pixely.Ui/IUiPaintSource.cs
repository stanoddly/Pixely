namespace Pixely.Ui;

/// <summary>
/// What a renderer is allowed to see of a <see cref="UiRoot"/>: the completed instructions, and the
/// facts needed to decide whether they are still current. Deliberately without <c>Update</c> and
/// <c>SetTargetSize</c>. Building the tree raises application callbacks, and a renderer may not
/// raise those: the renderers sharing a frame are entitled to domain data that does not change
/// underneath them. Same-assembly code can still cast back to <see cref="UiRoot"/>; the point is
/// that no ordinary edit reaches a build by accident.
/// </summary>
internal interface IUiPaintSource
{
    IReadOnlyList<PaintInstruction> Instructions { get; }

    IReadOnlyList<PaintBatch> Batches { get; }

    /// <summary>The size of the space the instructions are in, which is the size of the texture to paint them into.</summary>
    Vector2Int PaintedRasterSize { get; }

    /// <summary>The target size the instructions were built to be presented into.</summary>
    Vector2Int PaintedTargetSize { get; }

    /// <summary>How many target pixels one pixel of the painted texture covers when it is presented.</summary>
    float PaintedPresentScale { get; }

    /// <summary>The target size the root will build for next, which is stale to present into when it is not the painted one.</summary>
    Vector2Int TargetSize { get; }

    ulong PaintVersion { get; }
}
