namespace Pixely.Ui;

/// <summary>
/// Places an element's children. A layout owns <em>allocation</em> — orientation, gaps, slot
/// geometry, and how leftover space is divided among growing children. It does not own sizing
/// <em>mechanics</em>: turning a child's <see cref="Sizing"/> and margin into constraints, and
/// applying alignment when placing it, belong to the parent element and are reached through
/// <see cref="ILayoutHost"/>. That split is what stops every custom layout from having to
/// reimplement Grow, Percent and margins.
/// </summary>
public interface ILayout
{
    /// <summary>
    /// Returns the extent the children need, given the host's content constraints.
    /// </summary>
    Vector2Int MeasureChildren(ILayoutHost host, Constraints contentConstraints);

    /// <summary>
    /// Places every child inside <paramref name="contentBounds"/>. Must not measure.
    /// </summary>
    void ArrangeChildren(ILayoutHost host, Rectangle contentBounds);
}
