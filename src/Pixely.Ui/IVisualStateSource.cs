namespace Pixely.Ui;

/// <summary>
/// A control whose interaction state reaches the elements it contains. A <see cref="Label"/> finds
/// the nearest one above it, which is how a button tints its text on hover without knowing that
/// its content is text at all.
/// </summary>
/// <remarks>
/// An implementation must invalidate paint whenever its <see cref="VisualState"/> or its
/// <see cref="ContentForeground"/> changes: the elements below resolve their colour while
/// painting, and nothing else marks them dirty.
/// <see cref="Element.SetPaintProperty"/> does it for state kept in a field, and
/// <see cref="Element.InvalidatePaint"/> for state that is derived.
/// </remarks>
public interface IVisualStateSource
{
    VisualState VisualState { get; }

    /// <summary>
    /// What the text inside this control is drawn in. A <see cref="Label"/> given a colour of its
    /// own uses that instead — except when it is disabled, which this still answers.
    /// Never null: the nearest source answers, so one with nothing of its own returns a default
    /// rather than leaving the question to a source further out.
    /// </summary>
    StateColors ContentForeground { get; }
}
