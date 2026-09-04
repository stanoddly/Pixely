namespace Pixely.Ui;

/// <summary>
/// A node in the retained UI tree. The box model lives here rather than in wrapper types, so
/// padding a centred image needs one element rather than three, and <see cref="ILayout"/> is the
/// seam for adding new arrangements without changing this class.
/// </summary>
public class Element : ILayoutHost
{
    private ILayout _layout = StackLayout.Vertical;
    private Thickness _margin;
    private Thickness _padding;
    private Sizing _width = Sizing.Fit;
    private Sizing _height = Sizing.Fit;
    private Alignment _horizontalAlignment = Alignment.Start;
    private Alignment _verticalAlignment = Alignment.Start;
    private bool _isVisible = true;
    private bool _clipsContent;
    private bool _isEnabled = true;
    private Drawable? _background;

    private readonly List<Element> _layoutChildren = new();

    private bool _measureDirty = true;
    private bool _arrangeDirty = true;
    private bool _paintDirty = true;
    private bool _hasMeasured;
    private Constraints _measuredWith;
    private Constraints _contentConstraints;

    public Element()
    {
        Children = new ElementCollection(this, MaxChildCount);
    }

    /// <summary>Overridden by single-content controls to reject a second child where the mistake is made.</summary>
    protected virtual int MaxChildCount => int.MaxValue;

    public Element? Parent { get; internal set; }

    /// <summary>
    /// The root this element currently belongs to, or null when it is not in a rooted tree.
    /// Kept up to date as elements are added and removed, so an element can reach shared state such
    /// as <see cref="UiRoot.Style"/> without it being threaded through every constructor.
    /// </summary>
    internal UiRoot? OwnerRoot { get; private set; }

    internal void SetOwnerRoot(UiRoot? root)
    {
        if (ReferenceEquals(OwnerRoot, root))
        {
            return;
        }

        OwnerRoot = root;

        foreach (Element child in Children)
        {
            child.SetOwnerRoot(root);
        }
    }

    public ElementCollection Children { get; }

    public ILayout Layout
    {
        get => _layout;
        set => SetMeasureProperty(ref _layout, value ?? throw new ArgumentNullException(nameof(value)));
    }

    /// <summary>Space outside the element. May be negative, which shrinks the size it reports and can overlap siblings.</summary>
    public Thickness Margin
    {
        get => _margin;
        set => SetMeasureProperty(ref _margin, value);
    }

    /// <summary>Space between the element's edges and its content. Must not be negative.</summary>
    public Thickness Padding
    {
        get => _padding;
        set
        {
            if (value.HasNegativeEdge)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Padding must not have negative edges.");
            }

            SetMeasureProperty(ref _padding, value);
        }
    }

    public Sizing Width
    {
        get => _width;
        set => SetMeasureProperty(ref _width, value);
    }

    public Sizing Height
    {
        get => _height;
        set => SetMeasureProperty(ref _height, value);
    }

    public Alignment HorizontalAlignment
    {
        get => _horizontalAlignment;
        set => SetMeasureProperty(ref _horizontalAlignment, value);
    }

    public Alignment VerticalAlignment
    {
        get => _verticalAlignment;
        set => SetMeasureProperty(ref _verticalAlignment, value);
    }

    /// <summary>An invisible element takes no space and is skipped by layout entirely.</summary>
    public bool IsVisible
    {
        get => _isVisible;
        set => SetMeasureProperty(ref _isVisible, value);
    }

    /// <summary>
    /// Clips this element's content and children to its bounds. The element's own background is
    /// painted before the clip is applied, so a panel still fills its full bounds.
    /// </summary>
    public bool ClipsContent
    {
        get => _clipsContent;
        set => SetArrangeProperty(ref _clipsContent, value);
    }

    /// <summary>Fills the element's bounds, painted before any clip is applied.</summary>
    public Drawable? Background
    {
        get => _background;
        set => SetPaintProperty(ref _background, value);
    }

    /// <summary>Local enabled state. Use <see cref="IsEffectivelyEnabled"/> for the value that accounts for ancestors.</summary>
    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetPaintProperty(ref _isEnabled, value);
    }

    public bool IsEffectivelyEnabled
    {
        get
        {
            for (Element? element = this; element != null; element = element.Parent)
            {
                if (!element._isEnabled)
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>The size this element asked for, excluding its margin. Valid after a measure pass.</summary>
    public Vector2Int DesiredSize { get; private set; }

    /// <summary>The absolute rectangle this element occupies. Valid after an arrange pass.</summary>
    public Rectangle Bounds { get; private set; }

    /// <summary>
    /// The visible region of this element, being every clipping ancestor's bounds intersected.
    /// Reflects the <see cref="ClipsContent"/> chain only; a clip pushed by a custom drawable is
    /// a painting detail and deliberately does not appear here.
    /// </summary>
    public Rectangle EffectiveClip { get; private set; }

    internal bool IsMeasureDirty => _measureDirty;
    internal bool IsArrangeDirty => _arrangeDirty;
    internal bool IsPaintDirty => _paintDirty;

    internal Vector2Int Measure(Constraints constraints)
    {
        if (!_isVisible)
        {
            DesiredSize = default;
            _measureDirty = false;
            return DesiredSize;
        }

        if (_hasMeasured && !_measureDirty && _measuredWith == constraints)
        {
            return DesiredSize;
        }

        RebuildLayoutChildren();

        _measuredWith = constraints;
        _contentConstraints = constraints.Deflate(_padding);

        Vector2Int content = MeasureContent(_contentConstraints);
        Vector2Int size = new(
            SaturatingAdd(content.X, _padding.Horizontal),
            SaturatingAdd(content.Y, _padding.Vertical));

        DesiredSize = constraints.Clamp(size);
        _hasMeasured = true;
        _measureDirty = false;
        _arrangeDirty = true;
        return DesiredSize;
    }

    internal void Arrange(Rectangle bounds, Rectangle inheritedClip)
    {
        if (!_isVisible)
        {
            return;
        }

        Bounds = bounds;
        EffectiveClip = _clipsContent ? Intersect(inheritedClip, bounds) : inheritedClip;

        ArrangeContent(_padding.Deflate(bounds));

        _arrangeDirty = false;
        _paintDirty = false;
    }

    /// <summary>
    /// Paints this element and its subtree. Sealed: the background is painted before the clip is
    /// pushed, so <see cref="ClipsContent"/> clips content and children rather than the element
    /// itself, and <see cref="PaintContent"/> never paints children, so a subclass cannot break
    /// the clip nesting.
    /// </summary>
    internal void Paint(PaintContext context)
    {
        if (!_isVisible)
        {
            return;
        }

        if (_background != null)
        {
            Drawable background = _background;
            context.PaintIsolated(() => background.Paint(context, Bounds));
        }

        if (_clipsContent)
        {
            using ClipScope scope = context.PushClip(Bounds);
            PaintSelfAndChildren(context);
        }
        else
        {
            PaintSelfAndChildren(context);
        }
    }

    private void PaintSelfAndChildren(PaintContext context)
    {
        context.PaintIsolated(() => PaintContent(context));

        foreach (Element child in Children)
        {
            child.Paint(context);
        }
    }

    /// <summary>
    /// Paints the element's own content. Must not paint children; the traversal does that.
    /// </summary>
    protected virtual void PaintContent(PaintContext context)
    {
    }

    /// <summary>
    /// The element's own content extent. The base measures children; an element with intrinsic
    /// content of its own maximises against <see cref="MeasureChildren"/>, and one that must not
    /// be inflated by its children (a scroll view) measures them differently or not at all.
    /// Children are only ever measured through this method, so a subclass can always suppress it.
    /// </summary>
    protected virtual Vector2Int MeasureContent(Constraints constraints) => MeasureChildren(constraints);

    protected virtual void ArrangeContent(Rectangle contentBounds) => ArrangeChildren(contentBounds);

    protected Vector2Int MeasureChildren(Constraints constraints) => _layout.MeasureChildren(this, constraints);

    protected void ArrangeChildren(Rectangle contentBounds) => _layout.ArrangeChildren(this, contentBounds);

    // Propagation always starts at the parent, never short-circuiting on this element's own flag.
    // A node excluded from layout — a hidden one, whose Measure never runs — stays dirty forever,
    // so testing its own flag first would swallow the invalidation that makes it visible again.
    internal void InvalidateMeasure()
    {
        _measureDirty = true;
        _arrangeDirty = true;
        _paintDirty = true;

        for (Element? ancestor = Parent; ancestor != null; ancestor = ancestor.Parent)
        {
            if (ancestor._measureDirty)
            {
                return;
            }

            ancestor._measureDirty = true;
            ancestor._arrangeDirty = true;
            ancestor._paintDirty = true;
        }
    }

    internal void InvalidateArrange()
    {
        _arrangeDirty = true;
        _paintDirty = true;

        for (Element? ancestor = Parent; ancestor != null; ancestor = ancestor.Parent)
        {
            if (ancestor._arrangeDirty)
            {
                return;
            }

            ancestor._arrangeDirty = true;
            ancestor._paintDirty = true;
        }
    }

    internal void InvalidatePaint()
    {
        _paintDirty = true;

        for (Element? ancestor = Parent; ancestor != null; ancestor = ancestor.Parent)
        {
            if (ancestor._paintDirty)
            {
                return;
            }

            ancestor._paintDirty = true;
        }
    }

    protected void SetMeasureProperty<T>(ref T field, T value)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        InvalidateMeasure();
    }

    protected void SetArrangeProperty<T>(ref T field, T value)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        InvalidateArrange();
    }

    protected void SetPaintProperty<T>(ref T field, T value)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        InvalidatePaint();
    }

    private void RebuildLayoutChildren()
    {
        _layoutChildren.Clear();

        foreach (Element child in Children)
        {
            if (child.IsVisible)
            {
                _layoutChildren.Add(child);
            }
        }
    }

    private static Rectangle Intersect(Rectangle first, Rectangle second)
    {
        int left = Math.Max(first.X, second.X);
        int top = Math.Max(first.Y, second.Y);
        int right = Math.Min(first.X + first.Width, second.X + second.Width);
        int bottom = Math.Min(first.Y + first.Height, second.Y + second.Height);

        return new Rectangle(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }

    private static int SaturatingAdd(int left, int right)
    {
        long sum = (long)left + right;
        return sum > int.MaxValue ? int.MaxValue : (int)Math.Max(0, sum);
    }

    // --- ILayoutHost -------------------------------------------------------

    int ILayoutHost.ChildCount => _layoutChildren.Count;

    Element ILayoutHost.GetChild(int index) => _layoutChildren[index];

    Sizing ILayoutHost.GetResolvedSizing(int index, Orientation orientation)
    {
        Element child = _layoutChildren[index];
        Sizing sizing = orientation == Orientation.Horizontal ? child.Width : child.Height;
        return sizing.ResolveFor(_contentConstraints.IsAxisDefinite(orientation));
    }

    int? ILayoutHost.GetDefiniteContentExtent(Orientation orientation) => GetDefiniteContentExtent(orientation);

    Vector2Int ILayoutHost.MeasureChild(int index, Constraints available)
    {
        Element child = _layoutChildren[index];
        Constraints childConstraints = BuildChildConstraints(child, available, null, default);
        Vector2Int size = child.Measure(childConstraints);
        return AddMargin(size, child.Margin);
    }

    Vector2Int ILayoutHost.MeasureChildWithExtent(int index, Orientation orientation, int extent, Constraints available)
    {
        Element child = _layoutChildren[index];
        Constraints childConstraints = BuildChildConstraints(child, available, orientation, extent);
        Vector2Int size = child.Measure(childConstraints);
        return AddMargin(size, child.Margin);
    }

    void ILayoutHost.ArrangeChild(int index, Rectangle slot)
    {
        Element child = _layoutChildren[index];
        Rectangle inner = child.Margin.Deflate(slot);

        int x = AlignAxis(inner.X, inner.Width, child.DesiredSize.X, child.HorizontalAlignment);
        int y = AlignAxis(inner.Y, inner.Height, child.DesiredSize.Y, child.VerticalAlignment);

        child.Arrange(new Rectangle(x, y, child.DesiredSize.X, child.DesiredSize.Y), EffectiveClip);
    }

    private int? GetDefiniteContentExtent(Orientation orientation)
    {
        if (orientation == Orientation.Horizontal)
        {
            return _contentConstraints.IsWidthDefinite ? _contentConstraints.MaxWidth : null;
        }

        return _contentConstraints.IsHeightDefinite ? _contentConstraints.MaxHeight : null;
    }

    private Constraints BuildChildConstraints(Element child, Constraints available, Orientation? forcedAxis, int forcedExtent)
    {
        (int minWidth, int maxWidth) = ResolveChildAxis(
            child, Orientation.Horizontal, available.MaxWidth, child.Margin.Horizontal, forcedAxis, forcedExtent);

        (int minHeight, int maxHeight) = ResolveChildAxis(
            child, Orientation.Vertical, available.MaxHeight, child.Margin.Vertical, forcedAxis, forcedExtent);

        return new Constraints(minWidth, minHeight, maxWidth, maxHeight);
    }

    private (int Min, int Max) ResolveChildAxis(
        Element child,
        Orientation orientation,
        int availableMax,
        int margin,
        Orientation? forcedAxis,
        int forcedExtent)
    {
        if (forcedAxis == orientation)
        {
            int tight = Math.Max(0, forcedExtent - margin);
            return (tight, tight);
        }

        bool axisIsDefinite = _contentConstraints.IsAxisDefinite(orientation);
        Sizing sizing = (orientation == Orientation.Horizontal ? child.Width : child.Height).ResolveFor(axisIsDefinite);
        int? definiteExtent = GetDefiniteContentExtent(orientation);

        switch (sizing.Mode)
        {
            // Fixed is honoured exactly: it is not clamped to the space on offer, and overflow is
            // the parent's problem rather than something silently shrunk here.
            case SizingMode.Fixed:
                return (sizing.Pixels, sizing.Pixels);

            // Percent resolves against the host's committed content extent, never the running
            // budget, so two 50% siblings each get half of the host.
            case SizingMode.Percent:
            {
                int value = Math.Max(0, (int)MathF.Round(sizing.Factor * definiteExtent!.Value));
                return (value, value);
            }

            // A Grow child on the layout's own axis is always measured through a forced extent,
            // so a Grow that reaches here is on the cross axis, where it means "fill".
            case SizingMode.Grow:
                return Stretched(definiteExtent!.Value, margin);

            default:
            {
                Alignment alignment = orientation == Orientation.Horizontal
                    ? child.HorizontalAlignment
                    : child.VerticalAlignment;

                if (alignment == Alignment.Stretch && definiteExtent.HasValue)
                {
                    return Stretched(definiteExtent.Value, margin);
                }

                int max = availableMax == Constraints.Unbounded
                    ? Constraints.Unbounded
                    : Math.Max(0, availableMax - margin);

                return (0, max);
            }
        }
    }

    private static (int Min, int Max) Stretched(int definiteExtent, int margin)
    {
        int extent = Math.Max(0, definiteExtent - margin);
        return (extent, extent);
    }

    private static Vector2Int AddMargin(Vector2Int size, Thickness margin)
    {
        return new Vector2Int(
            Math.Max(0, size.X + margin.Horizontal),
            Math.Max(0, size.Y + margin.Vertical));
    }

    private static int AlignAxis(int offset, int available, int size, Alignment alignment)
    {
        return alignment switch
        {
            Alignment.Center => offset + (available - size) / 2,
            Alignment.End => offset + available - size,
            _ => offset
        };
    }
}
