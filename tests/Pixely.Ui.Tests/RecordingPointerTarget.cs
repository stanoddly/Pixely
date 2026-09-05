namespace Pixely.Ui.Tests;

/// <summary>
/// A pointer target that records what it was told, so tests can assert the callbacks a button
/// hides behind its own state: which of them arrive, in what order, and with what position.
/// </summary>
internal sealed class RecordingPointerTarget : Element, IPointerTarget
{
    public List<string> Calls { get; } = new();

    /// <summary>Runs inside the leave callback, which is where a target gets to route the pointer again.</summary>
    public Action? WhenLeft { get; set; }

    /// <inheritdoc cref="WhenLeft"/>
    public Action? WhenEntered { get; set; }

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

    void IPointerTarget.OnPointerPress(Vector2Int position) => Calls.Add($"press {position.X},{position.Y}");

    void IPointerTarget.OnPointerRelease(Vector2Int position, bool inside) => Calls.Add($"release {position.X},{position.Y} inside={inside}");

    void IPointerTarget.OnPointerCancel() => Calls.Add("cancel");
}
