using Pixely.Input;

namespace Pixely.Text;

/// <summary>What a key meant for the field it was typed into.</summary>
public enum TextEditingOutcome
{
    /// <summary>Not an editing key. The caller should let it through to whatever is underneath.</summary>
    Ignored,

    /// <summary>Used, and the buffer may have changed.</summary>
    Handled,

    /// <summary>The user asked to finish. Whether the value is acceptable is the control's decision.</summary>
    Commit,

    /// <summary>The user asked to abandon the edit.</summary>
    Cancel
}

/// <summary>
/// Turns a key press into an edit on a <see cref="TextEditingBuffer"/>. Everything here is about
/// what the key means; what to do about a finished or abandoned edit belongs to the control, which
/// is the part that differs between an immediate-mode field and a retained one.
/// </summary>
public static class TextEditingCommands
{
    public static TextEditingOutcome HandleKey(
        TextEditingBuffer buffer,
        Scancode scancode,
        bool shift,
        bool ctrl,
        IClipboardService? clipboard = null)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        switch (scancode)
        {
            case Scancode.Backspace:
                DeleteBackwards(buffer, ctrl);
                return TextEditingOutcome.Handled;

            case Scancode.Delete:
                DeleteForwards(buffer, ctrl);
                return TextEditingOutcome.Handled;

            case Scancode.Left:
                MoveCaret(buffer, shift, ctrl, forwards: false);
                return TextEditingOutcome.Handled;

            case Scancode.Right:
                MoveCaret(buffer, shift, ctrl, forwards: true);
                return TextEditingOutcome.Handled;

            case Scancode.Home:
                MoveCaretTo(buffer, 0, shift);
                return TextEditingOutcome.Handled;

            case Scancode.End:
                MoveCaretTo(buffer, buffer.Text.Length, shift);
                return TextEditingOutcome.Handled;

            case Scancode.A when ctrl:
                buffer.SelectionAnchor = 0;
                buffer.CursorPosition = buffer.Text.Length;
                return TextEditingOutcome.Handled;

            case Scancode.C when ctrl:
                if (buffer.HasSelection)
                {
                    clipboard?.SetText(buffer.GetSelectedText());
                }

                return TextEditingOutcome.Handled;

            case Scancode.X when ctrl:
                Cut(buffer, clipboard);
                return TextEditingOutcome.Handled;

            case Scancode.V when ctrl:
                Paste(buffer, clipboard);
                return TextEditingOutcome.Handled;

            case Scancode.Return:
            case Scancode.Return2:
            case Scancode.KeypadEnter:
                return TextEditingOutcome.Commit;

            case Scancode.Escape:
                return TextEditingOutcome.Cancel;

            default:
                return TextEditingOutcome.Ignored;
        }
    }

    private static void DeleteBackwards(TextEditingBuffer buffer, bool ctrl)
    {
        if (buffer.HasSelection)
        {
            buffer.TryDeleteSelection();
        }
        else if (ctrl)
        {
            int target = FindWordBoundaryLeft(buffer.Text, buffer.CursorPosition);
            buffer.TryRemove(target, buffer.CursorPosition - target);
        }
        else if (buffer.CursorPosition > 0)
        {
            buffer.TryRemove(buffer.CursorPosition - 1, 1);
        }
    }

    private static void DeleteForwards(TextEditingBuffer buffer, bool ctrl)
    {
        if (buffer.HasSelection)
        {
            buffer.TryDeleteSelection();
        }
        else if (ctrl)
        {
            int target = FindWordBoundaryRight(buffer.Text, buffer.CursorPosition);
            buffer.TryRemove(buffer.CursorPosition, target - buffer.CursorPosition);
        }
        else if (buffer.CursorPosition < buffer.Text.Length)
        {
            buffer.TryRemove(buffer.CursorPosition, 1);
        }
    }

    /// <summary>
    /// Moves the caret one character or one word. Without shift, an existing selection collapses to
    /// the edge being moved towards rather than the caret moving from where it happens to be — which
    /// is what makes an arrow key after selecting land where the selection ends.
    /// </summary>
    private static void MoveCaret(TextEditingBuffer buffer, bool shift, bool ctrl, bool forwards)
    {
        if (shift)
        {
            buffer.SelectionAnchor ??= buffer.CursorPosition;
            buffer.CursorPosition = NextPosition(buffer, ctrl, forwards);
            return;
        }

        if (buffer.HasSelection && !ctrl)
        {
            (int start, int length) = buffer.GetSelectionRange();
            buffer.CursorPosition = forwards ? start + length : start;
            buffer.SelectionAnchor = null;
            return;
        }

        buffer.SelectionAnchor = null;
        buffer.CursorPosition = NextPosition(buffer, ctrl, forwards);
    }

    private static int NextPosition(TextEditingBuffer buffer, bool ctrl, bool forwards)
    {
        if (forwards)
        {
            return ctrl
                ? FindWordBoundaryRight(buffer.Text, buffer.CursorPosition)
                : Math.Min(buffer.Text.Length, buffer.CursorPosition + 1);
        }

        return ctrl
            ? FindWordBoundaryLeft(buffer.Text, buffer.CursorPosition)
            : Math.Max(0, buffer.CursorPosition - 1);
    }

    private static void MoveCaretTo(TextEditingBuffer buffer, int position, bool shift)
    {
        if (shift)
        {
            buffer.SelectionAnchor ??= buffer.CursorPosition;
        }
        else
        {
            buffer.SelectionAnchor = null;
        }

        buffer.CursorPosition = position;
    }

    private static void Cut(TextEditingBuffer buffer, IClipboardService? clipboard)
    {
        if (!buffer.HasSelection)
        {
            return;
        }

        string selected = buffer.GetSelectedText();

        // Copied only once the delete has been allowed, so a cut the edit filter refused does not
        // silently replace what was on the clipboard.
        if (buffer.TryDeleteSelection())
        {
            clipboard?.SetText(selected);
        }
    }

    private static void Paste(TextEditingBuffer buffer, IClipboardService? clipboard)
    {
        string? text = clipboard?.GetText();

        if (text != null)
        {
            buffer.TryInsertText(text);
        }
    }

    private static int FindWordBoundaryLeft(string text, int position)
    {
        if (position <= 0)
        {
            return 0;
        }

        int i = position - 1;

        while (i > 0 && char.IsWhiteSpace(text[i]))
        {
            i--;
        }

        while (i > 0 && !char.IsWhiteSpace(text[i - 1]))
        {
            i--;
        }

        return i;
    }

    private static int FindWordBoundaryRight(string text, int position)
    {
        if (position >= text.Length)
        {
            return text.Length;
        }

        int i = position;

        while (i < text.Length && !char.IsWhiteSpace(text[i]))
        {
            i++;
        }

        while (i < text.Length && char.IsWhiteSpace(text[i]))
        {
            i++;
        }

        return i;
    }
}
