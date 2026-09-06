using Pixely.Input;

namespace Pixely.Ui.Tests;

/// <summary>
/// A focus target that records what it was told, and that can be made to act inside any of its
/// callbacks — which is where focus transitions get interesting, because that is where a field
/// commits and rebuilds whatever the route it interrupted was walking.
/// </summary>
internal sealed class RecordingFocusTarget : Element, IPointerTarget, IFocusTarget
{
    public List<string> Calls { get; } = new();

    /// <summary>Buttons this target takes. Everything else is declined.</summary>
    public HashSet<MouseButton> Accepts { get; } = new() { MouseButton.Left };

    public bool HandlesKeys { get; set; } = true;

    public Action? WhenFocused { get; set; }

    public Action? WhenBlurred { get; set; }

    void IPointerTarget.OnPointerEnter(Vector2Int position)
    {
    }

    void IPointerTarget.OnPointerLeave()
    {
    }

    bool IPointerTarget.OnPointerPress(Vector2Int position, MouseButton button) => Accepts.Contains(button);

    void IPointerTarget.OnPointerRelease(Vector2Int position, MouseButton button, bool inside) =>
        Calls.Add($"release {button} inside={inside}");

    void IPointerTarget.OnPointerCancel(MouseButton button) => Calls.Add($"cancel {button}");

    void IFocusTarget.OnFocusGained()
    {
        Calls.Add("focused");
        WhenFocused?.Invoke();
    }

    void IFocusTarget.OnFocusLost()
    {
        Calls.Add("blurred");
        WhenBlurred?.Invoke();
    }

    bool IFocusTarget.OnKeyDown(Scancode scancode, Keyboard keyboard, bool isRepeat)
    {
        Calls.Add($"key {scancode}{(isRepeat ? " repeat" : string.Empty)}");
        return HandlesKeys;
    }

    bool IFocusTarget.OnTextInput(string text)
    {
        Calls.Add($"text {text}");
        return HandlesKeys;
    }
}
