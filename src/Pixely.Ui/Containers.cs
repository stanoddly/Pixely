namespace Pixely.Ui;

/// <summary>Stacks children top to bottom.</summary>
public sealed class Column : Element
{
    public Column(int gap = 0) => Layout = new StackLayout(Orientation.Vertical, gap);
}

/// <summary>Stacks children left to right.</summary>
public sealed class Row : Element
{
    public Row(int gap = 0) => Layout = new StackLayout(Orientation.Horizontal, gap);
}

/// <summary>Places every child in the same space, painted in order.</summary>
public sealed class Overlay : Element
{
    public Overlay() => Layout = OverlayLayout.Instance;
}
