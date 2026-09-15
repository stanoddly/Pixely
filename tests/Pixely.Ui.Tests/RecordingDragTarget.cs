using Pixely.Input;

namespace Pixely.Ui.Tests;

/// <summary>A <see cref="RecordingPointerTarget"/> that also records the drags it is sent while it holds a press.</summary>
internal sealed class RecordingDragTarget : RecordingPointerTarget, IPointerDragTarget
{
    /// <inheritdoc cref="RecordingPointerTarget.WhenLeft"/>
    public Action? WhenDragged { get; set; }

    void IPointerDragTarget.OnPointerDrag(Vector2Int position, MouseButton button)
    {
        Calls.Add($"drag {position.X},{position.Y} {button}");
        WhenDragged?.Invoke();
    }
}
