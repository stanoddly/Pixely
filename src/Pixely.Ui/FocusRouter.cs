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
/// So every transition is validated against the tree afterwards rather than assumed to have held.
/// </remarks>
internal sealed class FocusRouter
{
    private readonly UiRoot _root;

    private Element? _focused;

    // Bumped by every transition, so a callback that moves focus itself can be told apart from one
    // that did not. Without it the transition it interrupted would finish and overwrite it.
    private int _routeVersion;

    internal FocusRouter(UiRoot root) => _root = root;

    internal Element? Focused => _focused;

    /// <summary>
    /// Moves focus to <paramref name="target"/>, or takes it away when that is null. Does nothing if
    /// the target already holds it, so a second press on a field does not commit and reopen it.
    /// </summary>
    internal void Focus(Element? target)
    {
        if (target != null && !_root.CanBeHit(target))
        {
            target = null;
        }

        if (ReferenceEquals(_focused, target))
        {
            return;
        }

        Element? previous = _focused;

        // Cleared before the callback rather than pointed at the destination, so a handler that
        // moves focus itself neither sees the new target as focused nor loses the old one twice.
        _focused = null;
        int version = ++_routeVersion;

        if (previous != null)
        {
            ((IFocusTarget)previous).OnFocusLost();
        }

        // Losing focus is where a field commits, and committing can rebuild whatever the incoming
        // target belonged to. This transition is abandoned rather than completed on top of that.
        if (target == null || _routeVersion != version || !_root.CanBeHit(target))
        {
            return;
        }

        _focused = target;
        ((IFocusTarget)target).OnFocusGained();

        // Gaining focus can detach the element just as losing it can, and leaving focus on something
        // unreachable would send it keys it can no longer act on.
        if (_routeVersion == version && !_root.CanBeHit(target))
        {
            _focused = null;
            ((IFocusTarget)target).OnFocusLost();
        }
    }

    internal bool KeyDown(Scancode scancode, Keyboard keyboard, bool isRepeat)
    {
        Element? target = Current();
        return target != null && ((IFocusTarget)target).OnKeyDown(scancode, keyboard, isRepeat);
    }

    internal bool TextInput(string text)
    {
        Element? target = Current();
        return target != null && ((IFocusTarget)target).OnTextInput(text);
    }

    /// <summary>
    /// Reconciles focus with a tree that has just been rebuilt, the way the pointer router reconciles
    /// hover: an element can be hidden, disabled or detached without any keyboard event happening, so
    /// nothing else would notice.
    /// </summary>
    internal void Revalidate() => Current();

    /// <summary>
    /// The focused element if it is still reachable, dropping it if it is not. Checked on every
    /// dispatch rather than only after a build, because a callback can detach the focused element and
    /// the next key would otherwise reach something no longer in the tree.
    /// </summary>
    private Element? Current()
    {
        if (_focused == null || _root.CanBeHit(_focused))
        {
            return _focused;
        }

        Element stale = _focused;
        _focused = null;
        _routeVersion++;
        ((IFocusTarget)stale).OnFocusLost();

        // The callback may have focused something else, which is the current answer.
        return _focused;
    }
}
