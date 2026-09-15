namespace Pixely.Ui;

/// <summary>The axes something scrolls along. Flags, so a grid scrolls both.</summary>
[Flags]
public enum ScrollAxes
{
    None = 0,
    Horizontal = 1,
    Vertical = 2,
    Both = Horizontal | Vertical
}
