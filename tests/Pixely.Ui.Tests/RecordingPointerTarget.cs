using Pixely.Input;

namespace Pixely.Ui.Tests;

/// <summary>
/// A pointer target that records what it was told, so tests can assert the callbacks a button
/// hides behind its own state: which of them arrive, in what order, and with what position.
/// </summary>
internal sealed class RecordingPointerTarget : Element, IPointerTarget
{
    public List<string> Calls { get; } = new();

    /// <summary>Buttons this target takes. Everything else is declined.</summary>
    public HashSet<MouseButton> Accepts { get; } = new() { MouseButton.Left };

    /// <summary>Runs inside the leave callback, which is where a target gets to route the pointer again.</summary>
    public Action? WhenLeft { get; set; }

    /// <inheritdoc cref="WhenLeft"/>
    public Action? WhenEntered { get; set; }

    /// <inheritdoc cref="WhenLeft"/>
    public Action? WhenPressed { get; set; }

    /// <inheritdoc cref="WhenLeft"/>
    public Action? WhenReleased { get; set; }

    /// <inheritdoc cref="WhenLeft"/>
    public Action? WhenCancelled { get; set; }

    void IPointerTarget.OnPointerEnter(Vector2Int position)
    {
        Calls.Add($"enter {position.X},{position.Y}");
        WhenEntered?.Invoke();
    }

    void IPointerTarget.OnPointerLeave()
    {
        Calls.Add("leave");
        WhenLeft?.Invoke();
    }

    bool IPointerTarget.OnPointerPress(Vector2Int position, MouseButton button)
    {
        if (!Accepts.Contains(button))
        {
            Calls.Add($"declined {button}");
            return false;
        }

        Calls.Add($"press {position.X},{position.Y} {button}");
        WhenPressed?.Invoke();
        return true;
    }

    void IPointerTarget.OnPointerRelease(Vector2Int position, MouseButton button, bool inside)
    {
        Calls.Add($"release {position.X},{position.Y} {button} inside={inside}");
        WhenReleased?.Invoke();
    }

    void IPointerTarget.OnPointerCancel(MouseButton button)
    {
        Calls.Add($"cancel {button}");
        WhenCancelled?.Invoke();
    }
}
