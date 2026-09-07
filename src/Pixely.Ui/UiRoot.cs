using Pixely.Input;

namespace Pixely.Ui;

/// <summary>
/// Drives measure, arrange and paint for one viewport, and owns the state the renderer reads.
/// Elements never run a layout pass on themselves, so the tree cannot be half-updated.
/// </summary>
public sealed class UiRoot
{
    private readonly PointerRouter _pointerRouter;
    private readonly PaintContext _paintContext = new();
    private readonly List<Element> _layers = new();
    private readonly List<PaintBatch> _batches = new();
    private readonly List<UiView> _views = new();
    private readonly List<Rectangle> _pointerTargetAreas = new();
    private readonly List<Element> _pointerTargetElements = new();

    private UiStyle? _style;
    private bool _isUpdating;
    private Vector2Int _viewportSize;
    private bool _layersChanged = true;

    // Starts where the router's position starts, so the first route to the origin is the non-event it
    // actually is rather than a change from nowhere.
    /// <summary>How many times reporting will chase a subscriber that invalidates what it was told.</summary>
    private const int MaxFocusReportRounds = 8;

    private bool _isReportingFocus;

    private Vector2Int _reportedPointerPosition;

    private readonly FocusRouter _focusRouter;
    private Element? _reportedFocus;

    private readonly ChangeNotifier<Vector2Int> _pointerPositionChanged = new();
    private readonly ChangeNotifier<Element?> _focusChanged = new();
    private readonly ChangeNotifier<Vector2Int> _viewportChanged = new();

    public UiRoot()
    {
        _pointerRouter = new PointerRouter(this);
        _focusRouter = new FocusRouter(this);
    }

    /// <summary>
    /// The viewport the completed instructions were built for. The renderer refuses to present
    /// instructions built for a different size, which is what keeps a resize from showing a frame
    /// laid out for the old one.
    /// </summary>
    internal Vector2Int PaintedViewportSize { get; private set; }

    internal bool IsPaintDirty { get; private set; } = true;

    internal IReadOnlyList<PaintInstruction> Instructions => _paintContext.Instructions;

    /// <summary>Instruction runs sharing a texture and a clip, in paint order.</summary>
    internal IReadOnlyList<PaintBatch> Batches => _batches;

    /// <summary>
    /// Where each pointer target can be hit, in paint order, alongside the targets themselves.
    /// Two lists rather than one of pairs: hit testing reads only the rectangles, and keeping them
    /// packed is the point of having the list at all.
    /// </summary>
    internal List<Rectangle> PointerTargetAreas => _pointerTargetAreas;

    internal List<Element> PointerTargetElements => _pointerTargetElements;

    /// <summary>
    /// Defaults every element under this root can fall back on. Elements that were given an
    /// explicit value ignore it. Replacing it invalidates the layers, since nothing below them
    /// holds a value that would otherwise change.
    /// </summary>
    public UiStyle? Style
    {
        get => _style;
        set
        {
            if (ReferenceEquals(_style, value))
            {
                return;
            }

            _style = value;

            foreach (Element layer in _layers)
            {
                layer.InvalidateSubtreeMeasure();
            }
        }
    }

    /// <summary>
    /// The clipboard every <see cref="TextBox"/> under this root reaches unless it was given one of
    /// its own. Defaults to <see cref="NullClipboardService"/>, which leaves copy and paste inert;
    /// <c>UseUi</c> replaces it with the application's.
    /// </summary>
    public IClipboardService Clipboard { get; set; } = NullClipboardService.Instance;

    public Vector2Int ViewportSize => _viewportSize;

    public IReadOnlyList<Element> Layers => _layers;

    /// <summary>Adds a layer on top of the existing ones.</summary>
    public void AddLayer(Element layer)
    {
        ArgumentNullException.ThrowIfNull(layer);

        if (layer.Parent != null)
        {
            throw new InvalidOperationException("The element is a child of another element; remove it from its parent first.");
        }

        // An element in two places at once would resolve its root through the one link it holds,
        // so the other place would read a style that is not the one it is painted under.
        if (layer.LayerRoot != null)
        {
            throw new InvalidOperationException("The element is already a layer on a root; remove it from that root first.");
        }

        _layers.Add(layer);
        layer.LayerRoot = this;
        layer.InvalidateSubtreeMeasure();
        _layersChanged = true;
    }

    /// <summary>
    /// Attaches a view — building its tree and subscribing it to its view model — and adds that
    /// tree as a layer. Attaching here rather than in the view's constructor is what keeps view
    /// constructors free of virtual calls.
    /// </summary>
    public void AddView(UiView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        view.Attach();

        try
        {
            _views.Add(view);
            AddLayer(view.Root);
        }
        catch
        {
            // A layer that was refused leaves a view attached to nothing: subscribed to its models
            // and syncing a tree no root will ever draw.
            _views.Remove(view);
            view.Detach();
            throw;
        }
    }

    /// <summary>Removes a view's layer and unsubscribes it from its view model.</summary>
    public bool RemoveView(UiView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        if (!_views.Remove(view))
        {
            return false;
        }

        try
        {
            RemoveLayer(view.Root);
        }
        finally
        {
            // A blur raised on the way out is application code. If it throws, the view has still been
            // removed, and leaving it subscribed to its view model would keep it syncing a tree that
            // is no longer on screen.
            view.Detach();
        }

        return true;
    }

    public bool RemoveLayer(Element layer)
    {
        ArgumentNullException.ThrowIfNull(layer);

        if (!_layers.Remove(layer))
        {
            return false;
        }

        layer.LayerRoot = null;
        _layersChanged = true;

        try
        {
            // Now, not at the next pass. A window closed on the way out may never run another one, and
            // whatever the removed subtree was holding would stay held: a gesture with no way to end,
            // and the platform's text input left running for a field that is gone.
            _pointerRouter.Revalidate();
            _focusRouter.Revalidate();
        }
        finally
        {
            ReportFocus();
        }

        return true;
    }

    /// <summary>
    /// Routes a pointer move. Returns true when the UI is taking the pointer, so the caller can
    /// keep the event from reaching whatever is underneath.
    /// </summary>
    /// <remarks>
    /// Hit testing reads the bounds the last <see cref="Update"/> produced, so a tree that has not
    /// been laid out yet hits nothing. There is one pointer: these are not per-device, and feeding
    /// two mice into them interleaves their gestures into one.
    /// </remarks>
    public bool PointerMoved(Vector2Int position)
    {
        bool consumed = _pointerRouter.Moved(position);
        ReportPointerPosition();
        return consumed;
    }

    /// <inheritdoc cref="PointerMoved"/>
    /// <remarks>
    /// Consumed only when a target took <paramref name="button"/>. A target that declines it is not
    /// captured and does not hide the press from whatever the UI is drawn over, so the buttons a
    /// screen does not use stay available to the game.
    /// </remarks>
    public bool PointerPressed(Vector2Int position, MouseButton button = MouseButton.Left)
    {
        bool consumed = _pointerRouter.Pressed(position, button);
        ReportPointerPosition();
        ReportFocus();
        return consumed;
    }

    /// <inheritdoc cref="PointerPressed"/>
    public bool PointerReleased(Vector2Int position, MouseButton button = MouseButton.Left)
    {
        bool consumed = _pointerRouter.Released(position, button);
        ReportPointerPosition();
        return consumed;
    }

    /// <summary>The pointer left the window, which cancels every press in progress.</summary>
    public void PointerLeft() => _pointerRouter.Left();

    /// <summary>The element taking keyboard input, if any.</summary>
    public Element? FocusedElement => _focusRouter.Focused;

    /// <summary>
    /// Moves focus, or takes it away when <paramref name="element"/> is null. An element that is not
    /// an <see cref="IFocusTarget"/>, or that input cannot reach, takes focus away instead of
    /// receiving it.
    /// </summary>
    public void Focus(Element? element)
    {
        try
        {
            _focusRouter.Focus(element as IFocusTarget == null ? null : element);
        }
        finally
        {
            // In a finally because a focus callback is application code: if it throws, focus has
            // already moved, and leaving that unreported would leave the platform's text input
            // running for an element that no longer holds anything.
            ReportFocus();
        }
    }

    /// <summary>
    /// Routes a key to whatever holds focus. Returns true when it was used, so the caller can keep
    /// the key from reaching whatever is underneath.
    /// </summary>
    public bool KeyPressed(Scancode scancode, Keyboard keyboard, bool isRepeat = false)
    {
        try
        {
            return _focusRouter.KeyDown(scancode, keyboard, isRepeat);
        }
        finally
        {
            ReportFocus();
        }
    }

    /// <inheritdoc cref="KeyPressed"/>
    public bool TextEntered(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        try
        {
            return _focusRouter.TextInput(text);
        }
        finally
        {
            ReportFocus();
        }
    }

    /// <summary>
    /// Raised when focus moves, with the element that now holds it or null. What starts and stops the
    /// platform's text input reads this: the decision belongs to the final state a route settled on,
    /// not to any of the transitions along the way.
    /// </summary>
    public event Action<Element?>? FocusChanged
    {
        add => _focusChanged.Add(value);
        remove => _focusChanged.Remove(value);
    }

    /// <summary>
    /// Whether input could still reach <paramref name="element"/>. A target that was hidden, disabled
    /// or detached mid-gesture has to lose whatever it holds: resuming when it comes back would turn
    /// a press the user made before into a click on something else, or send a key to a field that is
    /// no longer on screen.
    /// </summary>
    internal bool CanBeHit(Element element)
    {
        if (!ReferenceEquals(element.OwnerRoot, this))
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

    /// <summary>Where the pointer last was, in the same coordinates the tree is laid out in.</summary>
    public Vector2Int PointerPosition => _pointerRouter.Position;

    /// <summary>
    /// Raised when <see cref="PointerPosition"/> changes. For what follows the pointer without being
    /// under it — a tooltip is not a hit target, so no <see cref="IPointerTarget"/> callback reaches
    /// it — and cheap to answer, because moving something is an arrange and not a measure.
    /// </summary>
    public event Action<Vector2Int>? PointerPositionChanged
    {
        add => _pointerPositionChanged.Add(value);
        remove => _pointerPositionChanged.Remove(value);
    }

    /// <summary>
    /// Raised after the viewport changed and the layers were invalidated. Layout alone answers most
    /// of what a resize means, but not a position the application derives from the viewport itself:
    /// a popup anchored to something in the world is at a different place on screen afterwards, and
    /// nothing in the tree can work that out for it.
    /// </summary>
    public event Action<Vector2Int>? ViewportChanged
    {
        add => _viewportChanged.Add(value);
        remove => _viewportChanged.Remove(value);
    }

    /// <summary>
    /// Reports the pointer's position once the route that moved it has finished. Deliberately not
    /// from inside the route: a listener is free to route the pointer itself, and doing that partway
    /// through a press would let it take the capture the outer press is about to install, or hand the
    /// outer release a gesture that had only just begun.
    /// </summary>
    /// <remarks>
    /// A listener that does route again reports from its own nested call, which leaves nothing for
    /// this one to say. That is what keeps a later subscriber from being told a position two routes
    /// out of date, after an earlier one has already moved on.
    /// </remarks>
    /// <summary>
    /// Announces where focus ended up. Deliberately silent while a transition is still in flight: a
    /// field that commits and hands focus straight to the next one would otherwise be reported as
    /// focus leaving and something else taking it, and whatever drives the platform's text input
    /// would stop and restart it in between.
    /// </summary>
    private void ReportFocus()
    {
        // Only the outermost report reconciles. A subscriber is free to move focus, and every nested
        // report that started would otherwise chase it one level deeper until the stack ran out; the
        // loop below is what follows it instead, at one level.
        if (_isReportingFocus || _focusRouter.IsRouting)
        {
            return;
        }

        _isReportingFocus = true;

        try
        {
            // A subscriber can detach the element it was just told about, so what was reported is
            // settled against the tree again afterwards. Bounded for the same reason the routers are:
            // a subscriber free to keep doing that is not a sequence that converges.
            for (int round = 0; round < MaxFocusReportRounds; round++)
            {
                _focusRouter.Revalidate();
                Element? focused = _focusRouter.Focused;

                if (ReferenceEquals(_reportedFocus, focused))
                {
                    return;
                }

                _reportedFocus = focused;
                _focusChanged.Notify(focused);
            }

            // Out of rounds. Keeping focus where it is would be the kinder answer, but there is no
            // value that can be announced and still be true afterwards: announcing is a callback, and
            // this application's callbacks move focus every time they are asked.
            AbandonFocus();
        }
        catch
        {
            // A subscriber can move focus and then throw, which leaves what was last announced naming
            // an element that no longer holds anything and no round left to correct it. The same
            // terminal state settles that, best effort: an exception from announcing it would replace
            // the one the caller is already unwinding with, which is the more useful of the two.
            try
            {
                AbandonFocus();
            }
            catch
            {
                // Nothing to add.
            }

            throw;
        }
        finally
        {
            _isReportingFocus = false;
        }
    }

    /// <summary>
    /// Drops focus and says so, with the router held there while it is said. The last word on focus
    /// has to be one that nothing can contradict, and an announcement is a callback like any other:
    /// anything else it might be told could be made false by the telling.
    /// </summary>
    private void AbandonFocus()
    {
        _focusRouter.Abandon();
        _focusRouter.Freeze();

        try
        {
            if (_reportedFocus != null)
            {
                _reportedFocus = null;
                _focusChanged.Notify(null);
            }
        }
        finally
        {
            _focusRouter.Unfreeze();
        }
    }

    private void ReportPointerPosition()
    {
        Vector2Int position = _pointerRouter.Position;

        if (_reportedPointerPosition == position)
        {
            return;
        }

        _reportedPointerPosition = position;
        _pointerPositionChanged.Notify(position);
    }

    public void SetViewportSize(Vector2Int size)
    {
        if (_viewportSize == size)
        {
            return;
        }

        _viewportSize = size;

        foreach (Element layer in _layers)
        {
            layer.InvalidateMeasure();
        }

        _viewportChanged.Notify(size);
    }

    /// <summary>
    /// Brings the tree up to date if anything changed. Returns true when the instruction list was
    /// rebuilt, so the caller knows the retained texture needs repainting.
    /// </summary>
    public bool Update()
    {
        // Pointer callbacks run inside this method, and one of them calling back into it would
        // refill the paint context an outer pass is still writing to, duplicating every quad.
        if (_isUpdating || !NeedsUpdate())
        {
            return false;
        }

        _isUpdating = true;

        try
        {
            return Rebuild();
        }
        finally
        {
            _isUpdating = false;
            ReportFocus();
        }
    }

    private bool Rebuild()
    {

        Rectangle viewport = new(0, 0, _viewportSize.X, _viewportSize.Y);
        Constraints constraints = Constraints.Tight(_viewportSize);

        _paintContext.Reset(viewport);

        foreach (Element layer in _layers)
        {
            layer.Measure(constraints);
            layer.Arrange(viewport, viewport);
        }

        CollectPointerTargets();

        // Bounds have just moved under a pointer that did not, so what it is over is reconciled
        // between arrange and paint: the new bounds are needed to hit test at all, and painting
        // afterwards is what keeps this frame from showing a hover the tree no longer has.
        _pointerRouter.Revalidate();

        // Focus survives layout moving underneath it, but not the element leaving the tree, being
        // hidden or being disabled. None of those produces a keyboard event, so nothing else notices.
        _focusRouter.Revalidate();

        // A pointer callback may have restructured the tree, and the paint below draws it as it is
        // now. Collecting again keeps what can be hit matching what the frame shows; a hover that
        // only repaints leaves layout clean, so the ordinary case still collects once.
        if (NeedsLayout())
        {
            CollectPointerTargets();
        }

        foreach (Element layer in _layers)
        {
            layer.Paint(_paintContext);
        }

        PaintBatcher.Build(_paintContext.Instructions, _batches);

        _layersChanged = false;
        IsPaintDirty = false;
        PaintedViewportSize = _viewportSize;
        return true;
    }

    private void CollectPointerTargets()
    {
        _pointerTargetAreas.Clear();
        _pointerTargetElements.Clear();

        foreach (Element layer in _layers)
        {
            layer.CollectPointerTargets(_pointerTargetAreas, _pointerTargetElements);
        }
    }

    /// <summary>
    /// Whether a layer still needs measuring or arranging. Every structural edit invalidates
    /// measure, so this is what says a tree changed rather than only changing how it looks.
    /// </summary>
    private bool NeedsLayout()
    {
        foreach (Element layer in _layers)
        {
            if (layer.IsMeasureDirty || layer.IsArrangeDirty)
            {
                return true;
            }
        }

        return false;
    }

    private bool NeedsUpdate()
    {
        if (_layersChanged || IsPaintDirty || PaintedViewportSize != _viewportSize)
        {
            return true;
        }

        foreach (Element layer in _layers)
        {
            if (layer.IsMeasureDirty || layer.IsArrangeDirty || layer.IsPaintDirty)
            {
                return true;
            }
        }

        return false;
    }
}
