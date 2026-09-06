namespace Pixely.Ui;

/// <summary>
/// Turns pointer positions into the enter, leave, press, release and cancel an
/// <see cref="IPointerTarget"/> sees. Hit testing and capture live here rather than in
/// <see cref="UiRoot"/>, which only forwards.
/// </summary>
/// <remarks>
/// Hover is derived from the last known position rather than remembered, so it survives the tree
/// moving underneath a pointer that did not: <see cref="Revalidate"/> recomputes it after a build.
/// </remarks>
internal sealed class PointerRouter
{
    private readonly UiRoot _root;

    // Hit testing only ever returns elements that are pointer targets, which is what lets these be
    // held as elements: the router needs the element to know whether it is still in the tree.
    private Element? _hovered;
    private Element? _captured;

    private Vector2Int _position;
    private bool _isInWindow;

    // Bumped by every entry point, so a callback that routes the pointer again can be told apart
    // from one that did not. Without it the call it interrupted would finish and overwrite it.
    private int _routeVersion;

    internal PointerRouter(UiRoot root) => _root = root;

    internal bool Moved(Vector2Int position)
    {
        _routeVersion++;
        MoveTo(position);
        return Track();
    }

    internal bool Pressed(Vector2Int position)
    {
        _routeVersion++;
        MoveTo(position);

        // A press while another target holds capture cancels that one. Without this the first
        // target never hears how its press ended and stays pressed for good.
        Cancel();

        int version = _routeVersion;
        Element? target = HitTest(position);
        UpdateHover(target);

        // Cancel and hover callbacks can route the pointer themselves. Pressing on top of that would
        // hand capture to an element the current route has already moved away from.
        if (target == null || _routeVersion != version)
        {
            return false;
        }

        _captured = target;
        ((IPointerTarget)target).OnPointerPress(position);
        return true;
    }

    internal bool Released(Vector2Int position)
    {
        _routeVersion++;
        MoveTo(position);

        Element? captured = _captured;

        if (captured == null)
        {
            // Nothing to end, but the pointer is somewhere new and hover has to follow it there.
            Track();
            return false;
        }

        _captured = null;
        bool inside = ReferenceEquals(HitTest(position), captured);
        ((IPointerTarget)captured).OnPointerRelease(position, inside);

        // The callback is where a click is handled, so it may have rearranged the tree. Hover is
        // recomputed rather than reusing the hit above, which by now can name a detached element.
        Track();
        return true;
    }

    /// <summary>The pointer left the window, which cancels a press in progress.</summary>
    internal void Left()
    {
        _routeVersion++;
        _isInWindow = false;
        Cancel();
        Track();
    }

    /// <summary>
    /// Reconciles hover and capture with a tree that has just been rebuilt. Layout moves elements
    /// under a pointer that never moved, and a hovered or captured element can leave the tree
    /// altogether; neither produces a pointer event, so nothing else would notice.
    /// </summary>
    internal void Revalidate()
    {
        _routeVersion++;

        if (_captured != null && !CanBeHit(_captured))
        {
            Cancel();
        }

        Track();
    }

    /// <summary>
    /// Whether hit testing could still reach <paramref name="element"/>. A target that was hidden,
    /// disabled or detached mid-gesture has to lose capture: resuming when it comes back would turn
    /// a press the user made before into a click on something else.
    /// </summary>
    private bool CanBeHit(Element element)
    {
        if (!ReferenceEquals(element.OwnerRoot, _root))
        {
            return false;
        }

        for (Element? ancestor = element; ancestor != null; ancestor = ancestor.Parent)
        {
            if (!ancestor.IsVisible || !ancestor.IsEnabled)
            {
                return false;
            }
        }

        return true;
    }

    private void MoveTo(Vector2Int position)
    {
        _position = position;
        _isInWindow = true;
    }

    private bool Track()
    {
        Element? target = _isInWindow ? HitTest(_position) : null;

        // While a target holds capture it is the only one that can be hovered, which is what makes a
        // pressed button un-highlight when the pointer is dragged off it and light up again on return.
        if (_captured != null)
        {
            UpdateHover(ReferenceEquals(target, _captured) ? _captured : null);
            return true;
        }

        UpdateHover(target);
        return target != null;
    }

    private void Cancel()
    {
        Element? captured = _captured;

        if (captured == null)
        {
            return;
        }

        _captured = null;
        ((IPointerTarget)captured).OnPointerCancel();
    }

    private void UpdateHover(Element? target)
    {
        if (ReferenceEquals(_hovered, target))
        {
            return;
        }

        Element? previous = _hovered;

        // Cleared before the leave rather than pointed at the destination, so a callback that routes
        // again neither sees the new target as hovered nor leaves the old one a second time.
        _hovered = null;
        int version = _routeVersion;

        if (previous != null)
        {
            ((IPointerTarget)previous).OnPointerLeave();
        }

        // A leave callback may have routed the pointer itself, or detached the element this
        // transition was heading for. Either way that result is the current one, so this transition
        // is abandoned rather than completed on top of it.
        if (target == null || _routeVersion != version || !CanBeHit(target))
        {
            return;
        }

        _hovered = target;
        ((IPointerTarget)target).OnPointerEnter(_position);
    }

    /// <summary>
    /// The topmost target at <paramref name="position"/>. Scanned back to front over the areas the
    /// last build collected, which is paint order reversed: whatever was drawn on top is what the
    /// pointer meets first, and a child is allowed to overflow the element that arranged it because
    /// nothing is pruned by an ancestor's bounds.
    /// </summary>
    /// <remarks>
    /// The scan reads rectangles and nothing else, so it walks contiguous memory and touches no
    /// element until something is actually hit. Only then is the candidate checked against the tree
    /// it belongs to, which is where the list being one build old is accounted for.
    /// </remarks>
    private Element? HitTest(Vector2Int position)
    {
        List<Rectangle> areas = _root.PointerTargetAreas;

        for (int i = areas.Count - 1; i >= 0; i--)
        {
            if (!areas[i].Contains(position))
            {
                continue;
            }

            Element candidate = _root.PointerTargetElements[i];

            if (CanBeHit(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
