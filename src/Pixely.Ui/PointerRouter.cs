using Pixely.Input;

namespace Pixely.Ui;

/// <summary>
/// Turns pointer positions into the enter, leave, press, release and cancel an
/// <see cref="IPointerTarget"/> sees. Hit testing and capture live here rather than in
/// <see cref="UiRoot"/>, which only forwards.
/// </summary>
/// <remarks>
/// <para>
/// Hover is derived from the last known position rather than remembered, so it survives the tree
/// moving underneath a pointer that did not: <see cref="Revalidate"/> recomputes it after a build.
/// </para>
/// <para>
/// Capture is per button, because a release of one button must not end another's gesture. The
/// route version stays global: position and hover are shared, so a callback that presses a second
/// button, or merely moves the pointer, invalidates whatever route it interrupted whichever button
/// that route belonged to.
/// </para>
/// </remarks>
internal sealed class PointerRouter
{
    // MouseButton runs from 1 to 5, and the slot is the button's own value, so nothing has to map
    // between the two.
    private const int CaptureSlotCount = 6;

    private readonly UiRoot _root;

    // Hit testing only ever returns elements that are pointer targets, which is what lets these be
    // held as elements: the router needs the element to know whether it is still in the tree.
    private Element? _hovered;

    private readonly Element?[] _captured = new Element?[CaptureSlotCount];

    // Which capture came first, so hover has an owner when the left button is not one of them.
    private readonly int[] _captureTokens = new int[CaptureSlotCount];
    private int _lastCaptureToken;

    // Reused by CancelAll, which cannot stackalloc a span of elements and should not allocate on a
    // path the pointer reaches every time it leaves the window.
    private readonly Element?[] _cancelScratch = new Element?[CaptureSlotCount];

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

    internal bool Pressed(Vector2Int position, MouseButton button)
    {
        _routeVersion++;
        MoveTo(position);

        // Read before the cancel below, not after it: that cancel runs a callback which may route the
        // pointer itself, and a version taken afterwards would already include whatever it did. The
        // press would then continue on top of a nested one and overwrite the capture it installed,
        // leaving that gesture with no way to end.
        int version = _routeVersion;

        // A second press of the same button cancels the gesture it already holds. Without this the
        // first target never hears how its press ended and stays pressed for good.
        Cancel(button);

        if (_routeVersion != version)
        {
            return false;
        }

        Element? target = HitTest(position);
        UpdateHover(target);

        // Cancel and hover callbacks can route the pointer themselves. Pressing on top of that would
        // hand capture to an element the current route has already moved away from.
        if (target == null || _routeVersion != version)
        {
            return false;
        }

        // The callback runs before capture is installed, because it decides whether there is one.
        // That is the whole difference from a press that cannot be declined, and it is why
        // everything the callback may have changed is re-checked below.
        bool accepted = ((IPointerTarget)target).OnPointerPress(position, button);

        if (!accepted)
        {
            Track();
            return false;
        }

        // Accepted, but the world it accepted in may be gone: the callback may have routed again or
        // detached the target. A nested press of this same button needs no separate check, because
        // routing at all is what bumps the version.
        if (_routeVersion != version || !CanBeHit(target))
        {
            ((IPointerTarget)target).OnPointerCancel(button);

            // Hover can be left pointing at the element that just detached itself, and nothing else
            // routes until the next event.
            Track();
            return true;
        }

        _captured[(int)button] = target;
        _captureTokens[(int)button] = ++_lastCaptureToken;
        Track();
        return true;
    }

    internal bool Released(Vector2Int position, MouseButton button)
    {
        _routeVersion++;
        MoveTo(position);

        Element? captured = _captured[(int)button];

        if (captured == null)
        {
            // Nothing to end, but the pointer is somewhere new and hover has to follow it there.
            Track();
            return false;
        }

        _captured[(int)button] = null;
        bool inside = ReferenceEquals(HitTest(position), captured);
        ((IPointerTarget)captured).OnPointerRelease(position, button, inside);

        // The callback is where a click is handled, so it may have rearranged the tree. Hover is
        // recomputed rather than reusing the hit above, which by now can name a detached element.
        Track();
        return true;
    }

    /// <summary>The pointer left the window, which cancels every press in progress.</summary>
    internal void Left()
    {
        _routeVersion++;
        _isInWindow = false;
        CancelAll();
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

        for (int slot = 0; slot < CaptureSlotCount; slot++)
        {
            Element? captured = _captured[slot];

            // Re-read each slot rather than working from a list taken up front, because a cancel
            // callback can end this button's gesture and start another one. Reachability is what
            // decides, so a replacement the callback just installed is kept and an unreachable one
            // is not, whichever order they arrived in.
            if (captured != null && !CanBeHit(captured))
            {
                Cancel((MouseButton)slot);
            }
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
        Element? owner = HoverOwner();

        // While a gesture is in progress its target is the only one that can be hovered, which is
        // what makes a pressed button un-highlight when the pointer is dragged off it and light up
        // again on return.
        if (owner != null)
        {
            UpdateHover(ReferenceEquals(target, owner) ? owner : null);
            return true;
        }

        UpdateHover(target);
        return target != null;
    }

    /// <summary>
    /// The capture hover belongs to: the left button when it holds one, and otherwise the oldest
    /// capture still standing. Left wins outright because it is the button whose press-and-drag
    /// feedback a user is watching; the age rule only decides between the others.
    /// </summary>
    private Element? HoverOwner()
    {
        Element? left = _captured[(int)MouseButton.Left];

        if (left != null)
        {
            return left;
        }

        Element? oldest = null;
        int oldestToken = int.MaxValue;

        for (int slot = 0; slot < CaptureSlotCount; slot++)
        {
            if (_captured[slot] != null && _captureTokens[slot] < oldestToken)
            {
                oldest = _captured[slot];
                oldestToken = _captureTokens[slot];
            }
        }

        return oldest;
    }

    /// <summary>
    /// Ends every gesture that was in flight when this was called. Read out first and then cancelled
    /// by identity, because a cancel callback can start a gesture of its own: without the snapshot
    /// whether that one survives would depend on where its button sorts against the ones still to be
    /// swept, which is not a rule anyone could rely on.
    /// </summary>
    private void CancelAll()
    {
        Array.Copy(_captured, _cancelScratch, CaptureSlotCount);

        // In button order, so a target holding two of them hears about them predictably.
        for (int slot = 0; slot < CaptureSlotCount; slot++)
        {
            Element? held = _cancelScratch[slot];

            // Cleared as it goes, so the scratch never keeps an element alive past this sweep.
            _cancelScratch[slot] = null;

            if (held != null)
            {
                Cancel((MouseButton)slot, held);
            }
        }
    }

    private void Cancel(MouseButton button)
    {
        Element? captured = _captured[(int)button];

        if (captured != null)
        {
            Cancel(button, captured);
        }
    }

    /// <param name="expected">
    /// The capture this cancel was decided on. A callback earlier in the same sweep can have ended
    /// this button's gesture and started another, and cancelling that one would end a gesture that
    /// has only just begun.
    /// </param>
    private void Cancel(MouseButton button, Element expected)
    {
        int slot = (int)button;

        if (!ReferenceEquals(_captured[slot], expected))
        {
            return;
        }

        _captured[slot] = null;
        ((IPointerTarget)expected).OnPointerCancel(button);
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
        version = _routeVersion;
        ((IPointerTarget)target).OnPointerEnter(_position);

        // The enter callback may have removed the element it was just sent to. A later route would
        // eventually notice, but only by sending a leave to something no longer in the tree.
        if (_routeVersion == version && !CanBeHit(target))
        {
            _hovered = null;
            ((IPointerTarget)target).OnPointerLeave();
        }
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
