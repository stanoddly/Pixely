using Pixely.Input;

namespace Pixely.Ui;

/// <summary>
/// An element that takes keyboard input. The root owns which one holds focus and when it loses it,
/// so an implementer only decides what the keys mean.
/// </summary>
/// <remarks>
/// <para>
/// Focus follows an accepted left press, and every other left press takes it away — a press on
/// something that is not a focus target, one the target declined, or one that hit nothing at all.
/// That is what lets a field commit what was typed into it before the button the user clicked next
/// acts on the value.
/// </para>
/// <para>
/// Losing focus is not the same as being told to commit: the element is told that focus went, and
/// what to do about a half-finished edit is its own decision.
/// </para>
/// </remarks>
public interface IFocusTarget
{
    void OnFocusGained();

    void OnFocusLost();

    /// <param name="isRepeat">
    /// Whether the platform produced this because the key is held rather than newly pressed. Moving
    /// a caret and deleting want repeats; anything that toggles a mode does not.
    /// </param>
    /// <returns>Whether this element used the key, which is what keeps it from reaching the game.</returns>
    bool OnKeyDown(Scancode scancode, Keyboard keyboard, bool isRepeat);

    /// <summary>
    /// Text the platform has committed, already assembled from however many keystrokes it took.
    /// </summary>
    /// <returns>Whether this element used the text.</returns>
    bool OnTextInput(string text);
}
