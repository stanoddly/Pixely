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
        CursorPosition = text.Length;
    }

    public string Text { get; private set; }

    public int CursorPosition { get; set; }

    /// <summary>Where a selection started, or null when there is none. The caret is its other end.</summary>
    public int? SelectionAnchor { get; set; }

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
    /// Replaces the text outright, as an external value arriving rather than something typed. The
    /// caret goes to the end and any selection is dropped, and no edit filter applies: the value came
    /// from the application, which is not restricted to what a user could have typed.
    /// </summary>
    public void Reset(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        Text = text;
        CursorPosition = text.Length;
        SelectionAnchor = null;
    }

    private bool TryReplace(int start, int length, string replacement)
    {
        string candidate = Text.Remove(start, length).Insert(start, replacement);

        if (_acceptsEdit != null && !_acceptsEdit(candidate))
        {
            return false;
        }

        Text = candidate;
        CursorPosition = start + replacement.Length;
        SelectionAnchor = null;
        return true;
    }
}
