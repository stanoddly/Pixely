using System.Numerics;

namespace Pixely.Ui;

/// <summary>When a <see cref="ScrollView"/> shows a bar on an axis it scrolls.</summary>
public enum ScrollBarVisibility
{
    /// <summary>Only while the content overflows on that axis.</summary>
    Auto,

    Hidden,

    Visible
}

/// <summary>
/// A container that shows a window onto children larger than itself. A <see cref="Column"/> that
/// clips, with the children slid by <see cref="ScrollOffset"/>: the layout is the ordinary one and
/// may be replaced, so a horizontal strip is a scroll view with a horizontal stack and
/// <see cref="ScrollAxes.Horizontal"/>.
/// </summary>
/// <remarks>
/// <para>
/// On a scroll axis the children are measured without a bound, so a Grow child there is measured
/// to its content and a Percent child likewise; on the other axis they get what the scroll view
/// itself was given, so a Grow child of a vertical list fills its width as it would in a column.
/// The scroll view's own size is ordinary: a Fit one takes what its children need up to what it is
/// offered and only scrolls when that is less, and one whose scroll axis is unbounded from outside
/// as well grows to its children and never scrolls.
/// </para>
/// <para>
/// Padding scrolls with the children, the way a padding box does on the web: the extent is the
/// children plus the padding, and the last row has the bottom padding under it at the end.
/// </para>
/// <para>
/// The bars lie over the content along the trailing edges and take no space from it, which is what
/// keeps measuring single-pass. They are adornments rather than children, so <see cref="Element.Children"/>
/// holds only what the consumer put there.
/// </para>
/// </remarks>
public sealed class ScrollView : Element, IScrollTarget
{
    private const int DefaultWheelStep = 40;

    private readonly ScrollBar _horizontalBar;
    private readonly ScrollBar _verticalBar;

    private ScrollAxes _axes = ScrollAxes.Vertical;
    private ScrollBarVisibility _scrollBars = ScrollBarVisibility.Auto;
    private int _wheelStep = DefaultWheelStep;
    private Vector2Int _scrollOffset;
    private Vector2Int _childrenExtent;
    private Vector2Int _maxScrollOffset;

    // Wheel notches too small to move a pixel yet, banked until they add up to one. Cleared whenever
    // they would otherwise be applied to something other than the gesture that produced them.
    private float _remainderX;
    private float _remainderY;

    // What ScrollIntoView asked for, held until the build that can answer it. See ArrangeContent.
    private Element? _scrollIntoViewRequest;

    public ScrollView(int gap = 0)
    {
        Layout = new StackLayout(Orientation.Vertical, gap);
        ClipsContent = true;

        _horizontalBar = new ScrollBar(this, Orientation.Horizontal);
        _verticalBar = new ScrollBar(this, Orientation.Vertical);
        AttachAdornment(_horizontalBar);
        AttachAdornment(_verticalBar);
    }

    /// <summary>
    /// Which axes are scrollable. A measure property: the children are measured without a bound on
    /// these axes, and with the scroll view's own constraints on the others.
    /// </summary>
    public ScrollAxes Axes
    {
        get => _axes;
        set
        {
            if (_axes == value)
            {
                return;
            }

            // Banked under the old axes, so a re-enabled axis does not carry part of an old gesture.
            ClearRemainders();
            SetMeasureProperty(ref _axes, value);
        }
    }

    /// <summary>When the bars are shown. Applies to the axes in <see cref="Axes"/>; a bar never shows on one that does not scroll.</summary>
    public ScrollBarVisibility ScrollBars
    {
        get => _scrollBars;
        set => SetArrangeProperty(ref _scrollBars, value);
    }

    /// <summary>Pixels per wheel notch. Behaviour rather than look, so it lives here and not in the style.</summary>
    public int WheelStep
    {
        get => _wheelStep;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);

            if (_wheelStep == value)
            {
                return;
            }

            // Banked in pixels of the old step, so they must not mix with pixels of the new one.
            ClearRemainders();
            _wheelStep = value;
        }
    }

    /// <summary>
    /// How far the children are slid, in pixels towards the start. Clamped into
    /// <c>[0, MaxScrollOffset]</c> when the tree is next built, so assigning <see cref="int.MaxValue"/>
    /// means the end once laid out, and after the build this reads the real position, the way
    /// <see cref="Element.Bounds"/> does. An arrange property: scrolling re-arranges this subtree and
    /// measures nothing.
    /// </summary>
    public Vector2Int ScrollOffset
    {
        get => _scrollOffset;
        set => SetArrangeProperty(ref _scrollOffset, value);
    }

    /// <summary>The children plus the padding, per axis. Valid after a build.</summary>
    public Vector2Int ScrollExtent => new(SaturatingAdd(_childrenExtent.X, Padding.Horizontal), SaturatingAdd(_childrenExtent.Y, Padding.Vertical));

    /// <summary>The size of the window onto the children, which is this element's own. Valid after a build.</summary>
    public Vector2Int ViewportSize => new(Bounds.Width, Bounds.Height);

    /// <summary>How far <see cref="ScrollOffset"/> can go: the extent past the viewport, and zero on an axis that does not scroll. Valid after a build.</summary>
    public Vector2Int MaxScrollOffset => _maxScrollOffset;

    /// <summary>The offset within the range of the last build, which <see cref="ScrollOffset"/> is not while it holds a request the build has not settled.</summary>
    internal Vector2Int ClampedScrollOffset => Clamp(_scrollOffset);

    /// <summary>
    /// Moves by <paramref name="delta"/> within the range of the last build, and returns whether the
    /// offset changed. Synchronous on purpose, which is what lets the wheel say whether it was used;
    /// the price is a range one build old, so children added since are reached by the next call.
    /// </summary>
    public bool ScrollBy(Vector2Int delta)
    {
        // The offset may hold a request the build has not clamped yet, and moving from that would
        // read int.MaxValue as a position rather than as the end it stands for.
        Vector2Int current = Clamp(_scrollOffset);
        Vector2Int target = Clamp(new Vector2Int(Saturate((long)current.X + delta.X), Saturate((long)current.Y + delta.Y)));

        if (target == current)
        {
            return false;
        }

        SetArrangeProperty(ref _scrollOffset, target);
        return true;
    }

    /// <summary>
    /// Scrolls the least distance that brings <paramref name="descendant"/> wholly into the viewport
    /// on each axis in <see cref="Axes"/>, or its start when it is larger than the viewport. Resolved
    /// by the next build, from the geometry that build produces, so it is right after a change of
    /// size or content that has not been laid out yet; <see cref="ScrollOffset"/> reads the result
    /// after that build. Applied on top of an offset assigned before the build. A descendant that has
    /// left this subtree or been hidden by then is not scrolled to.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="descendant"/> is not below this scroll view.</exception>
    public void ScrollIntoView(Element descendant)
    {
        ArgumentNullException.ThrowIfNull(descendant);

        if (!IsDescendant(descendant))
        {
            throw new ArgumentException("The element is not a descendant of this scroll view.", nameof(descendant));
        }

        _scrollIntoViewRequest = descendant;
        InvalidateArrange();
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Per axis: refused when the delta points past an end the view is already at, so an inner view
    /// does not bank what it turned away and reversing direction responds to the first notch; taken
    /// otherwise, including a fraction that moves nothing yet. A step the range clamps discards the
    /// remainder as well. A view that scrolls only sideways takes the vertical component as a
    /// horizontal one, since a plain wheel produces nothing else: rolling towards the user moves to
    /// the right, as it moves down elsewhere.
    /// </remarks>
    ScrollAxes IScrollTarget.OnScroll(Vector2Int position, Vector2 delta)
    {
        Vector2Int current = Clamp(_scrollOffset);
        ScrollAxes taken = ScrollAxes.None;
        int stepX = 0;
        int stepY = 0;

        // A wheel rolled away from the user, a positive Y, asks for what is above, which is a smaller
        // offset; one tilted to the right, a positive X, asks for what is to the right, a larger one.
        float horizontalPixels = delta.X * _wheelStep;
        float verticalPixels = -delta.Y * _wheelStep;

        if (_axes == ScrollAxes.Horizontal)
        {
            // Summed into one request, so the end refuses the sum and one remainder banks it. A
            // component that is not a number is left out here so that it does not spoil the other,
            // and refused below by not being reported as taken.
            bool hasX = float.IsFinite(horizontalPixels) && horizontalPixels != 0f;
            bool hasY = float.IsFinite(verticalPixels) && verticalPixels != 0f;
            float pixels = (hasX ? horizontalPixels : 0f) + (hasY ? verticalPixels : 0f);

            if (Accept(ScrollAxes.Horizontal, pixels, current.X, _maxScrollOffset.X, ref _remainderX, out stepX))
            {
                taken = (hasX ? ScrollAxes.Horizontal : ScrollAxes.None) | (hasY ? ScrollAxes.Vertical : ScrollAxes.None);
            }
        }
        else
        {
            if (Accept(ScrollAxes.Horizontal, horizontalPixels, current.X, _maxScrollOffset.X, ref _remainderX, out stepX))
            {
                taken |= ScrollAxes.Horizontal;
            }

            if (Accept(ScrollAxes.Vertical, verticalPixels, current.Y, _maxScrollOffset.Y, ref _remainderY, out stepY))
            {
                taken |= ScrollAxes.Vertical;
            }
        }

        if (stepX != 0 || stepY != 0)
        {
            ScrollBy(new Vector2Int(stepX, stepY));
            Vector2Int landed = Clamp(_scrollOffset);

            if (landed.X != current.X + stepX)
            {
                _remainderX = 0f;
            }

            if (landed.Y != current.Y + stepY)
            {
                _remainderY = 0f;
            }
        }

        return taken;
    }

    /// <param name="pixels">How far the offset was asked to move on this axis, already in pixels.</param>
    private bool Accept(ScrollAxes axis, float pixels, int current, int max, ref float remainder, out int step)
    {
        step = 0;

        if ((_axes & axis) == 0 || pixels == 0f || !float.IsFinite(pixels))
        {
            return false;
        }

        if ((pixels < 0f && current <= 0) || (pixels > 0f && current >= max))
        {
            remainder = 0f;
            return false;
        }

        remainder += pixels;
        step = (int)Math.Clamp(remainder, int.MinValue, int.MaxValue);
        remainder -= step;
        return true;
    }

    private void ClearRemainders()
    {
        _remainderX = 0f;
        _remainderY = 0f;
    }

    protected override Constraints ResolveContentConstraints(Constraints constraints)
    {
        if ((_axes & ScrollAxes.Horizontal) != 0)
        {
            constraints = constraints.WithUnboundedWidth();
        }

        if ((_axes & ScrollAxes.Vertical) != 0)
        {
            constraints = constraints.WithUnboundedHeight();
        }

        return constraints;
    }

    protected override Vector2Int MeasureContent(Constraints constraints)
    {
        _childrenExtent = MeasureChildren(constraints);

        // Their rectangles are decided at arrange. Measured here so that they go through the same
        // lifecycle as any element and their measure flags are cleared.
        _horizontalBar.Measure(Constraints.Tight(default));
        _verticalBar.Measure(Constraints.Tight(default));

        return _childrenExtent;
    }

    protected override void ArrangeContent(Rectangle contentBounds)
    {
        Vector2Int extent = ScrollExtent;
        _maxScrollOffset = new Vector2Int(
            (_axes & ScrollAxes.Horizontal) != 0 ? Math.Max(0, extent.X - Bounds.Width) : 0,
            (_axes & ScrollAxes.Vertical) != 0 ? Math.Max(0, extent.Y - Bounds.Height) : 0);

        // Written back without invalidating: this is the build settling a request, not a change.
        _scrollOffset = Clamp(_scrollOffset);

        // The padded content box slid by the offset and grown to the children. Larger than the
        // children only matters to Center and End alignment; anything that could fill was resolved
        // at measure.
        Rectangle canvas = new(
            contentBounds.X - _scrollOffset.X,
            contentBounds.Y - _scrollOffset.Y,
            Math.Max(_childrenExtent.X, contentBounds.Width),
            Math.Max(_childrenExtent.Y, contentBounds.Height));

        ArrangeChildren(canvas);

        // Answered here, where the descendant's bounds are those of this very build, and by arranging
        // the children again rather than invalidating: an invalidation from inside a build shows its
        // change a frame late.
        if (_scrollIntoViewRequest != null)
        {
            Vector2Int target = ResolveScrollIntoView(_scrollIntoViewRequest);
            _scrollIntoViewRequest = null;

            if (target != _scrollOffset)
            {
                canvas = canvas with { X = contentBounds.X - target.X, Y = contentBounds.Y - target.Y };
                _scrollOffset = target;
                ArrangeChildren(canvas);
            }
        }

        ArrangeBars();
    }

    /// <summary>
    /// The offset that shows <paramref name="descendant"/>, from its bounds as just arranged against
    /// this element's, which are the window: the padding scrolls with the children and is no part
    /// of it. A request for an element no longer below here, or under something hidden, whose
    /// bounds are then whatever they last were, resolves to the offset as it stands.
    /// </summary>
    private Vector2Int ResolveScrollIntoView(Element descendant)
    {
        if (!IsDescendant(descendant) || !IsArrangedBelow(descendant))
        {
            return _scrollOffset;
        }

        Rectangle bounds = descendant.Bounds;
        int x = (_axes & ScrollAxes.Horizontal) != 0 ? Reveal(_scrollOffset.X, _maxScrollOffset.X, bounds.X, bounds.Width, Bounds.X, Bounds.Width) : _scrollOffset.X;
        int y = (_axes & ScrollAxes.Vertical) != 0 ? Reveal(_scrollOffset.Y, _maxScrollOffset.Y, bounds.Y, bounds.Height, Bounds.Y, Bounds.Height) : _scrollOffset.Y;
        return new Vector2Int(x, y);
    }

    /// <summary>
    /// On one axis: the least move that brings the span into the window. Behind the window's start,
    /// the start is aligned; past its end, the end is, unless the span is longer than the window, in
    /// which case its start is what is shown.
    /// </summary>
    private static int Reveal(int offset, int max, int start, int length, int windowStart, int windowLength)
    {
        long end = (long)start + length;
        long windowEnd = (long)windowStart + windowLength;
        long delta = 0;

        if (start < windowStart)
        {
            delta = start - windowStart;
        }
        else if (end > windowEnd)
        {
            delta = Math.Min(end - windowEnd, start - windowStart);
        }

        return (int)Math.Clamp(offset + delta, 0, max);
    }

    private bool IsDescendant(Element element)
    {
        for (Element? ancestor = element.Parent; ancestor != null; ancestor = ancestor.Parent)
        {
            if (ReferenceEquals(ancestor, this))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether the arrange that just ran reached <paramref name="descendant"/>: a hidden element is not arranged, and neither is anything under it.</summary>
    private bool IsArrangedBelow(Element descendant)
    {
        for (Element? element = descendant; element != null && !ReferenceEquals(element, this); element = element.Parent)
        {
            if (!element.IsVisible)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Along the trailing edges, inside the bounds, each shortened by the other when both show so
    /// they meet at an empty corner. A bar that is not shown is arranged to an empty rectangle,
    /// which paints nothing and can be hit nowhere, rather than hidden: toggling visibility during
    /// a build would invalidate the build.
    /// </summary>
    private void ArrangeBars()
    {
        int thickness = Math.Min(Style.Scroll.Thickness, Math.Min(Bounds.Width, Bounds.Height));
        bool showHorizontal = ShowsBar(ScrollAxes.Horizontal, _maxScrollOffset.X);
        bool showVertical = ShowsBar(ScrollAxes.Vertical, _maxScrollOffset.Y);

        Rectangle horizontal = showHorizontal
            ? new Rectangle(Bounds.X, Bounds.Y + Bounds.Height - thickness, Bounds.Width - (showVertical ? thickness : 0), thickness)
            : default;

        Rectangle vertical = showVertical
            ? new Rectangle(Bounds.X + Bounds.Width - thickness, Bounds.Y, thickness, Bounds.Height - (showHorizontal ? thickness : 0))
            : default;

        _horizontalBar.Arrange(horizontal, EffectiveClip);
        _verticalBar.Arrange(vertical, EffectiveClip);
    }

    private bool ShowsBar(ScrollAxes axis, int maxOffset)
    {
        if ((_axes & axis) == 0)
        {
            return false;
        }

        return _scrollBars switch
        {
            ScrollBarVisibility.Visible => true,
            ScrollBarVisibility.Auto => maxOffset > 0,
            _ => false
        };
    }

    private Vector2Int Clamp(Vector2Int offset) => new(Math.Clamp(offset.X, 0, _maxScrollOffset.X), Math.Clamp(offset.Y, 0, _maxScrollOffset.Y));

    private static int Saturate(long value) => (int)Math.Clamp(value, int.MinValue, int.MaxValue);

    private static int SaturatingAdd(int left, int right) => Saturate((long)left + right);
}
