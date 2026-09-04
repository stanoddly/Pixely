namespace Pixely.Ui;

/// <summary>
/// Drives measure, arrange and paint for one viewport, and owns the state the renderer reads.
/// Elements never run a layout pass on themselves, so the tree cannot be half-updated.
/// </summary>
public sealed class UiRoot
{
    private readonly PaintContext _paintContext = new();
    private readonly List<Element> _layers = new();
    private readonly List<PaintBatch> _batches = new();
    private readonly List<UiView> _views = new();

    private Vector2Int _viewportSize;
    private bool _layersChanged = true;

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
    /// Defaults every element under this root can fall back on. Elements that were given an
    /// explicit value ignore it.
    /// </summary>
    public UiStyle? Style { get; set; }

    public Vector2Int ViewportSize => _viewportSize;

    public IReadOnlyList<Element> Layers => _layers;

    /// <summary>Adds a layer on top of the existing ones.</summary>
    public void AddLayer(Element layer)
    {
        ArgumentNullException.ThrowIfNull(layer);

        _layers.Add(layer);
        layer.SetOwnerRoot(this);
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
        _views.Add(view);
        AddLayer(view.Root);
    }

    /// <summary>Removes a view's layer and unsubscribes it from its view model.</summary>
    public bool RemoveView(UiView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        if (!_views.Remove(view))
        {
            return false;
        }

        RemoveLayer(view.Root);
        view.Detach();
        return true;
    }

    public bool RemoveLayer(Element layer)
    {
        ArgumentNullException.ThrowIfNull(layer);

        if (!_layers.Remove(layer))
        {
            return false;
        }

        layer.SetOwnerRoot(null);
        _layersChanged = true;
        return true;
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
    }

    /// <summary>
    /// Brings the tree up to date if anything changed. Returns true when the instruction list was
    /// rebuilt, so the caller knows the retained texture needs repainting.
    /// </summary>
    public bool Update()
    {
        if (!NeedsUpdate())
        {
            return false;
        }

        Rectangle viewport = new(0, 0, _viewportSize.X, _viewportSize.Y);
        Constraints constraints = Constraints.Tight(_viewportSize);

        _paintContext.Reset(viewport);

        foreach (Element layer in _layers)
        {
            layer.Measure(constraints);
            layer.Arrange(viewport, viewport);
            layer.Paint(_paintContext);
        }

        PaintBatcher.Build(_paintContext.Instructions, _batches);

        _layersChanged = false;
        IsPaintDirty = false;
        PaintedViewportSize = _viewportSize;
        return true;
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
