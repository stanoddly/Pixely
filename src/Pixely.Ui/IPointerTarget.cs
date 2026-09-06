namespace Pixely.Ui;

/// <summary>
/// An element that takes pointer input. The root owns hit testing and capture, so an implementer
/// only decides what its own states mean: which of them is a click, and how each one looks.
/// </summary>
/// <remarks>
/// <para>
/// Implementing this is what makes an element solid to the pointer. Everything else is transparent
/// to it, however opaque it looks, so a panel does not swallow clicks meant for what is behind it —
/// which also means a modal backdrop has to be an element of its own rather than a background.
/// </para>
/// <para>
/// Deliberately without a move notification. Anything that tracks the pointer while it is held —
/// a slider, a scrollbar — needs a second interface, and that one can be added without changing
/// what a button has to implement.
/// </para>
/// </remarks>
public interface IPointerTarget
{
    void OnPointerEnter(Vector2Int position);

    void OnPointerLeave();

    void OnPointerPress(Vector2Int position);

    /// <param name="inside">
    /// Whether the release landed on this element, which is what separates a click from a press the
    /// user dragged away and cancelled.
    /// </param>
    void OnPointerRelease(Vector2Int position, bool inside);

    /// <summary>
    /// The press ended without a release this element can be told about: the pointer left the
    /// window, another press took capture, or the element left the tree. Whatever the press started
    /// is abandoned, and there is no position to report because none of these happened anywhere.
    /// </summary>
    void OnPointerCancel();
}
