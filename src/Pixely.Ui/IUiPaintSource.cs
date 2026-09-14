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

    /// <summary>The logical size the instructions are laid out in, which is the size to paint them at.</summary>
    Vector2Int PaintedViewportSize { get; }

    /// <summary>The target size the instructions were built to be presented into.</summary>
    Vector2Int PaintedTargetSize { get; }

    /// <summary>How many target pixels one logical pixel covers in the completed instructions.</summary>
    float PaintedScale { get; }

    /// <summary>The target size the root will build for next, which is stale to present into when it is not the painted one.</summary>
    Vector2Int TargetSize { get; }

    ulong PaintVersion { get; }
}
