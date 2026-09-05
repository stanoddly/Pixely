namespace Pixely.Ui.Tests;

/// <summary>
/// An element with intrinsic content, used to give layout tests something with a natural size and
/// to observe how often it is actually measured and arranged.
/// </summary>
internal sealed class MeasuredBox : Element
{
    private Vector2Int _intrinsicSize;

    public MeasuredBox()
    {
    }

    public MeasuredBox(int width, int height)
    {
        _intrinsicSize = new Vector2Int(width, height);
    }

    public Vector2Int IntrinsicSize
    {
        get => _intrinsicSize;
        set => SetMeasureProperty(ref _intrinsicSize, value);
    }

    public int MeasureCount { get; private set; }

    public int ArrangeCount { get; private set; }

    public Constraints LastConstraints { get; private set; }

    protected override Vector2Int MeasureContent(Constraints constraints)
    {
        MeasureCount++;
        LastConstraints = constraints;

        Vector2Int children = MeasureChildren(constraints);
        return new Vector2Int(
            Math.Max(_intrinsicSize.X, children.X),
            Math.Max(_intrinsicSize.Y, children.Y));
    }

    protected override void ArrangeContent(Rectangle contentBounds)
    {
        ArrangeCount++;
        base.ArrangeContent(contentBounds);
    }
}
