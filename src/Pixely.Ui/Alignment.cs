namespace Pixely.Ui;

/// <summary>
/// How an element sits inside the space its parent gave it.
/// </summary>
public enum Alignment
{
    Start,
    Center,
    End,

    /// <summary>
    /// Fill the offered extent. Ignored when the element declares an explicit size, and degraded
    /// to <see cref="Start"/> when the parent's own extent on that axis is indefinite.
    /// </summary>
    Stretch
}
