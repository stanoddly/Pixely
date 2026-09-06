namespace Pixely.Ui;

/// <summary>
/// A single-child container that clips its content to its own bounds. Exists to exercise clipping
/// on its own, before anything as involved as a scroll view depends on it.
/// </summary>
public sealed class ClipBorder : Element
{
    public ClipBorder()
    {
        ClipsContent = true;
    }

    protected override int MaxChildCount => 1;

    public Element? Content
    {
        get => Children.Count == 0 ? null : Children[0];
        set
        {
            Children.Clear();

            if (value != null)
            {
                Children.Add(value);
            }
        }
    }
}
