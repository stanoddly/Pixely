using Pixely.Input;

namespace Pixely.Ui;

/// <summary>
/// A pointer target that follows the pointer while it holds a press: a slider, a scrollbar's thumb.
/// Separate from <see cref="IPointerTarget"/> so that a button does not implement what only these use.
/// </summary>
public interface IPointerDragTarget : IPointerTarget
{
    /// <summary>
    /// The pointer moved while this element holds <paramref name="button"/>. Sent for every move
    /// between the accepted press and its release or cancel, wherever the pointer is, including
    /// outside this element; a button held by another element is reported to that element alone.
    /// </summary>
    void OnPointerDrag(Vector2Int position, MouseButton button);
}
