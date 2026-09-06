using Pixely.Input;

namespace Pixely.Ui;

/// <summary>
/// Owns which element holds focus and what reaches it. Sibling to <see cref="PointerRouter"/>, and
/// for the same reason: the transitions are re-entrant, and keeping them in one place is what stops
/// every caller having to get them right.
/// </summary>
/// <remarks>
/// The hazard here is that focus changes hands inside a pointer press, and a callback on the way
/// out — a field committing what was typed — can restructure the tree that press is still walking.
/// So focus is settled against the tree after every transition rather than assumed to have held,
/// and a callback that moves focus itself is what the transition it interrupted defers to.
/// </remarks>
internal sealed class FocusRouter
{
    /// <summary>
    /// How many times settling will send a blur before giving up, for the same reason hover has a
    /// bound: a handler is free to make its element reachable again and then unreachable again, and
    /// nothing about that sequence says it converges.
    /// </summary>
    private const int MaxSettlingRounds = 8;

    private readonly UiRoot _root;

    private Element? _focused;

    // Bumped by every transition, so a callback that moves focus itself can be told apart from one
    // that did not. Without it the transition it interrupted would finish and overwrite it.
    private int _routeVersion;

    private bool _isSettling;
    private bool _isFrozen;
    private int _busyDepth;

    internal FocusRouter(UiRoot root) => _root = root;

    internal Element? Focused => _focused;

    /// <summary>
    /// Whether this router is partway through anything — a transition, a settle, or a dispatch whose
    /// callback is still running. What reports focus outward waits for this to clear, so a handoff is
    /// seen as one move rather than as focus leaving and something else taking it, and so nothing
    /// half-finished is announced as where focus ended up.
    /// </summary>
    internal bool IsRouting => _busyDepth > 0;

    /// <summary>
    /// Moves focus to <paramref name="target"/>, or takes it away when that is null. Does nothing if
    /// the target already holds it, so a second press on a field does not commit and reopen it.
    /// </summary>
    internal void Focus(Element? target)
    {
        // Refused while frozen. See Freeze.
        if (_isFrozen)
        {
            return;
        }

        _busyDepth++;

        try
        {
            Route(target);
            Settle();
        }
        finally
        {
            _busyDepth--;
        }
    }

    internal bool KeyDown(Scancode scancode, Keyboard keyboard, bool isRepeat)
    {
        _busyDepth++;

        try
        {
            Element? target = Current();
            return target != null && ((IFocusTarget)target).OnKeyDown(scancode, keyboard, isRepeat);
        }
        finally
        {
            _busyDepth--;
        }
    }

    internal bool TextInput(string text)
    {
        _busyDepth++;

        try
        {
            Element? target = Current();
            return target != null && ((IFocusTarget)target).OnTextInput(text);
        }
        finally
        {
            _busyDepth--;
        }
    }

    /// <summary>
    /// Drops focus without telling anyone, for a caller that has run out of ways to settle it.
    /// </summary>
    internal void Abandon() => _focused = null;

    /// <summary>
    /// Refuses to move focus until <see cref="Unfreeze"/>. For announcing a final state: an
    /// announcement is a callback like any other and can move focus again, so the only value that
    /// stays true while it is being made is one that nothing is allowed to change.
    /// </summary>
    internal void Freeze() => _isFrozen = true;

    internal void Unfreeze() => _isFrozen = false;

    /// <summary>
    /// Reconciles focus with a tree that has just been rebuilt, the way the pointer router reconciles
    /// hover: an element can be hidden, disabled or detached without any keyboard event happening, so
    /// nothing else would notice.
    /// </summary>
    internal void Revalidate()
    {
        _busyDepth++;

        try
        {
            Settle();
        }
        finally
        {
            _busyDepth--;
        }
    }

    private void Route(Element? target)
    {
        if (target != null && !_root.CanBeHit(target))
        {
            target = null;
        }

        // Advanced before the no-op check below, not after it. A callback that asks for focus it
        // already has is still saying where focus belongs, and a transition further out that carried
        // on because this looked like nothing would then move focus somewhere the callback rejected.
        int version = ++_routeVersion;

        if (ReferenceEquals(_focused, target))
        {
            return;
        }

        Element? previous = _focused;

        // Cleared before the callback rather than pointed at the destination, so a handler that
        // moves focus itself neither sees the new target as focused nor loses the old one twice.
        _focused = null;

        if (previous != null)
        {
            ((IFocusTarget)previous).OnFocusLost();
        }

        // Losing focus is where a field commits, and committing can rebuild whatever the incoming
        // target belonged to. A nested transition is the current answer, so this one is abandoned
        // rather than completed on top of it; Settle then checks what that answer left behind.
        if (target == null || _routeVersion != version || !_root.CanBeHit(target))
        {
            return;
        }

        _focused = target;
        ((IFocusTarget)target).OnFocusGained();
    }

    /// <summary>
    /// Leaves focus on something input can still reach, or on nothing. Every path into this router
    /// ends here rather than trusting what a callback left, because a callback can settle a nested
    /// transition and then detach the element that transition chose.
    /// </summary>
    private void Settle()
    {
        // Only the outermost transition settles. The blur below moves focus in the very case this
        // exists for, and letting each nested transition settle too would spend the bound one level
        // deep at a time instead of on the cycle it is counting.
        if (_isSettling)
        {
            return;
        }

        _isSettling = true;

        try
        {
            for (int round = 0; _focused != null && !_root.CanBeHit(_focused); round++)
            {
                if (round == MaxSettlingRounds)
                {
                    _focused = null;
                    break;
                }

                Element stale = _focused;
                _focused = null;
                _routeVersion++;
                ((IFocusTarget)stale).OnFocusLost();
            }
        }
        finally
        {
            _isSettling = false;
        }
    }

    /// <summary>
    /// The focused element if input can still reach it. Settled on every dispatch rather than only
    /// after a build, because a callback can detach the focused element and the next key would
    /// otherwise reach something no longer in the tree.
    /// </summary>
    private Element? Current()
    {
        Settle();

        // Checked again rather than trusting the settle above, which does nothing when an outer one
        // is already running. That outer settle will finish the job, but not before this dispatch
        // would otherwise have handed a key to an element input can no longer reach.
        return _focused != null && _root.CanBeHit(_focused) ? _focused : null;
    }
}
