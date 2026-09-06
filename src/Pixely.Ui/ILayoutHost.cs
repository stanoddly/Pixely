namespace Pixely.Ui;

/// <summary>
/// The sizing mechanics of an element, exposed to its <see cref="ILayout"/>. Every size a layout
/// asks for comes back with the child's margin already accounted for and its <see cref="Sizing"/>
/// already applied, so a layout never interprets sizing rules itself.
/// </summary>
public interface ILayoutHost
{
    int ChildCount { get; }

    /// <summary>Children that are laid out. Invisible children are excluded.</summary>
    Element GetChild(int index);

    /// <summary>
    /// The child's sizing on the given axis <em>after</em> definiteness degradation, so a layout
    /// can tell a live Grow from one that degraded to Fit without repeating that rule.
    /// </summary>
    Sizing GetResolvedSizing(int index, Orientation orientation);

    /// <summary>
    /// The host's own committed content extent on an axis, or null when it is indefinite. This is
    /// the reference for Percent children and the budget Grow divides — deliberately not the
    /// running allocation budget, so two 50% siblings each get half of the host rather than half
    /// of successively smaller remainders.
    /// </summary>
    int? GetDefiniteContentExtent(Orientation orientation);

    /// <summary>
    /// Measures a child against the space still available, returning its size including margin.
    /// <paramref name="available"/> bounds Fit children only: Fixed and Percent are defined
    /// independently of allocation, so neither the remaining budget nor its maximum shrinks them.
    /// </summary>
    Vector2Int MeasureChild(int index, Constraints available);

    /// <summary>
    /// Measures a child whose extent on <paramref name="orientation"/> the layout has decided,
    /// where <paramref name="extent"/> is the slot size including the child's margin. Used for
    /// Grow children once their share is known.
    /// </summary>
    Vector2Int MeasureChildWithExtent(int index, Orientation orientation, int extent, Constraints available);

    /// <summary>
    /// Places a child in <paramref name="slot"/>, applying its margin and alignment. Never measures.
    /// </summary>
    void ArrangeChild(int index, Rectangle slot);
}
