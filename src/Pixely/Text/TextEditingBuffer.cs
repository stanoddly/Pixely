namespace Pixely.Text;

/// <summary>
/// The text a field is being edited into, with a caret and a selection. Holds no policy: what a
/// valid edit is comes from the caller, and when the edit is finished is not its business at all.
/// </summary>
/// <remarks>
/// Separate from any control so the immediate-mode and retained-mode fields share one of these
/// rather than each growing its own caret arithmetic and word navigation.
/// </remarks>
public sealed class TextEditingBuffer
{
    private readonly Func<string, bool>? _acceptsEdit;

    private int _cursorPosition;
    private int? _selectionAnchor;

    /// <param name="acceptsEdit">
    /// Whether a candidate string may be typed. Called for every edit including the half-finished
    /// ones, so a numeric field can refuse a letter without refusing the minus sign that has no digits
    /// after it yet. Null accepts everything.
    /// </param>
    public TextEditingBuffer(string text, Func<string, bool>? acceptsEdit = null)
    {
        ArgumentNullException.ThrowIfNull(text);

        _acceptsEdit = acceptsEdit;
        Text = text;
        _cursorPosition = text.Length;
    }

    public string Text { get; private set; }

    /// <summary>
    /// Where the caret sits, always within the text. Clamped rather than rejected: callers compute
    /// these by walking the text, and one position past either end is what that walk naturally
    /// produces at the ends.
    /// </summary>
    public int CursorPosition
    {
        get => _cursorPosition;
        set => _cursorPosition = Math.Clamp(value, 0, Text.Length);
    }

    /// <summary>Where a selection started, or null when there is none. The caret is its other end.</summary>
    public int? SelectionAnchor
    {
        get => _selectionAnchor;
        set => _selectionAnchor = value == null ? null : Math.Clamp(value.Value, 0, Text.Length);
    }

    public bool HasSelection => SelectionAnchor != null && SelectionAnchor.Value != CursorPosition;

    public (int Start, int Length) GetSelectionRange()
    {
        if (SelectionAnchor == null)
        {
            return (CursorPosition, 0);
        }

        int start = Math.Min(SelectionAnchor.Value, CursorPosition);
        int end = Math.Max(SelectionAnchor.Value, CursorPosition);
        return (start, end - start);
    }

    public string GetSelectedText()
    {
        (int start, int length) = GetSelectionRange();
        return length == 0 ? string.Empty : Text.Substring(start, length);
    }

    /// <summary>Replaces the selection, or inserts at the caret when there is none.</summary>
    public bool TryInsertText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        (int start, int length) = GetSelectionRange();
        return TryReplace(start, length, text);
    }

    public bool TryDeleteSelection()
    {
        (int start, int length) = GetSelectionRange();
        return length != 0 && TryReplace(start, length, string.Empty);
    }

    public bool TryRemove(int start, int length) => TryReplace(start, length, string.Empty);

    /// <summary>
    /// Replaces the text outright, starting the edit again from a new value. The caret goes to the
    /// end and any selection is dropped, and no edit filter applies: the value came from the
    /// application, which is not restricted to what a user could have typed.
    /// </summary>
    /// <remarks>
    /// For deliberately restarting an edit, not for an external value arriving while one is in
    /// progress — a field being typed into goes on showing what was typed, and a value assigned
    /// underneath it is what the edit is compared against when it finishes.
    /// </remarks>
    public void Reset(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        Text = text;
        _cursorPosition = text.Length;
        _selectionAnchor = null;
    }

    private bool TryReplace(int start, int length, string replacement)
    {
        string candidate = Text.Remove(start, length).Insert(start, replacement);

        if (_acceptsEdit != null && !_acceptsEdit(candidate))
        {
            return false;
        }

        Text = candidate;
        _cursorPosition = start + replacement.Length;
        _selectionAnchor = null;
        return true;
    }
}
