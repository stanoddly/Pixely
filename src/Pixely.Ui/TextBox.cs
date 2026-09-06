using Pixely.Gpu;
using Pixely.Input;
using Pixely.Text;

namespace Pixely.Ui;

/// <summary>
/// A single line of editable text. Shows <see cref="Text"/> until it is focused, and what is being
/// typed after that; which of those the application sees is decided when the edit finishes.
/// </summary>
/// <remarks>
/// <para>
/// The edit is a separate value from <see cref="Text"/> on purpose. A field being typed into goes on
/// showing what was typed even when the application assigns to it underneath, because the alternative
/// is a value changing under someone's hands mid-word.
/// </para>
/// <para>
/// Not sealed: <see cref="AcceptsEdit"/> and <see cref="CanCommit"/> are where a field that only
/// takes some values — a number — says so, and everything else about editing is shared.
/// </para>
/// </remarks>
public class TextBox : Element, IPointerTarget, IFocusTarget
{
    private readonly Font? _font;

    private string _text = string.Empty;
    private Color _color = Colors.White;
    private TextEditingBuffer? _editor;

    // How far the text is slid left so the caret stays in view. Paint owns it: it is the only place
    // that knows how wide anything is.
    private int _scrollOffset;

    public TextBox(Font? font = null)
    {
        _font = font;
        Padding = new Thickness(4, 2);
    }

    /// <summary>Raised when an edit finishes with a value this field accepts.</summary>
    public event Action<string>? Committed;

    protected override int MaxChildCount => 0;

    /// <summary>
    /// The value. Assigning while the field is focused leaves what is being typed alone; it is the
    /// value the edit is compared against when it finishes, not a replacement for it.
    /// </summary>
    public string Text
    {
        get => _text;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            SetMeasureProperty(ref _text, value);
        }
    }

    public Color Color
    {
        get => _color;
        set => SetPaintProperty(ref _color, value);
    }

    /// <summary>What is on screen: the edit while there is one, and the value otherwise.</summary>
    public string DisplayText => _editor?.Text ?? _text;

    /// <summary>Whether an edit is in progress.</summary>
    public bool IsEditing => _editor != null;

    /// <summary>Whether a candidate may be typed, including the half-finished states on the way.</summary>
    protected virtual bool AcceptsEdit(string candidate) => true;

    /// <summary>Whether an edit may finish with this value. A refused commit keeps the field focused.</summary>
    protected virtual bool CanCommit(string text) => true;

    /// <summary>Called with a value this field accepted, before <see cref="Committed"/> is raised.</summary>
    protected virtual void OnCommitted(string text)
    {
    }

    protected override Vector2Int MeasureContent(Constraints constraints)
    {
        Font font = ResolveFont();

        // Measured against the value rather than what is being typed, so a field does not resize
        // itself with every keystroke and shuffle everything beside it along.
        int height = font.CreateTextSprite("Ay").Size.Y;
        int width = _text.Length == 0 ? 0 : font.CreateTextSprite(_text).Size.X;
        return new Vector2Int(width, height);
    }

    protected override void PaintContent(PaintContext context)
    {
        Font font = ResolveFont();
        Rectangle content = Padding.Deflate(Bounds);
        string display = DisplayText;
        int caret = _editor?.CursorPosition ?? 0;

        _scrollOffset = ScrollToShowCaret(font, display, caret, content.Width);
        int originX = content.X - _scrollOffset;

        using ClipScope scope = context.PushClip(context.CurrentClip.Intersect(content));

        if (_editor != null && _editor.HasSelection)
        {
            (int start, int length) = _editor.GetSelectionRange();
            int selectionX = originX + TextWidth(font, display, start);
            int selectionWidth = TextWidth(font, display.Substring(start, length));
            context.FillRectangle(new Rectangle(selectionX, content.Y, selectionWidth, content.Height), SelectionColor());
        }

        if (display.Length > 0)
        {
            TextSpriteAsset sprite = font.CreateTextSprite(display);
            context.DrawSprite(sprite, new Rectangle(originX, content.Y, sprite.Size.X, sprite.Size.Y), _color);
        }

        if (_editor != null)
        {
            context.FillRectangle(new Rectangle(originX + TextWidth(font, display, caret), content.Y, 1, content.Height), _color);
        }
    }

    void IPointerTarget.OnPointerEnter(Vector2Int position)
    {
    }

    void IPointerTarget.OnPointerLeave()
    {
    }

    // Left only, and taken rather than declined: taking it is what focuses the field.
    bool IPointerTarget.OnPointerPress(Vector2Int position, MouseButton button) => button == MouseButton.Left;

    void IPointerTarget.OnPointerRelease(Vector2Int position, MouseButton button, bool inside)
    {
    }

    void IPointerTarget.OnPointerCancel(MouseButton button)
    {
    }

    void IFocusTarget.OnFocusGained()
    {
        _editor = new TextEditingBuffer(_text, AcceptsEdit);
        _scrollOffset = 0;
        InvalidatePaint();
    }

    void IFocusTarget.OnFocusLost()
    {
        if (_editor == null)
        {
            return;
        }

        // An edit that cannot finish is thrown away rather than forced on the application, which is
        // what makes clicking away from a half-typed number leave the old one in place.
        if (CanCommit(_editor.Text))
        {
            Commit();
        }
        else
        {
            Discard();
        }
    }

    bool IFocusTarget.OnKeyDown(Scancode scancode, Keyboard keyboard, bool isRepeat)
    {
        if (_editor == null)
        {
            return false;
        }

        TextEditingOutcome outcome = TextEditingCommands.HandleKey(_editor, scancode, keyboard.Shift, keyboard.Ctrl, Clipboard);

        switch (outcome)
        {
            case TextEditingOutcome.Ignored:
                return false;

            case TextEditingOutcome.Commit:
                // A value this field will not take keeps the caret where it is, so the user is left
                // looking at what needs fixing rather than at it being silently discarded.
                if (CanCommit(_editor.Text))
                {
                    Commit();
                    OwnerRoot?.Focus(null);
                }

                break;

            case TextEditingOutcome.Cancel:
                Discard();
                OwnerRoot?.Focus(null);
                break;
        }

        InvalidatePaint();
        return true;
    }

    bool IFocusTarget.OnTextInput(string text)
    {
        if (_editor == null)
        {
            return false;
        }

        _editor.TryInsertText(text);
        InvalidatePaint();
        return true;
    }

    /// <summary>The clipboard editing shortcuts use, or null when there is none to reach.</summary>
    protected virtual IClipboardService? Clipboard => null;

    private void Commit()
    {
        string value = _editor!.Text;

        // Cleared before anything is raised, so a handler that assigns to this field or moves focus
        // out of it is not fighting an edit that is still notionally in progress.
        _editor = null;
        SetMeasureProperty(ref _text, value);
        OnCommitted(value);
        Committed?.Invoke(value);
    }

    private void Discard()
    {
        _editor = null;
        InvalidatePaint();
    }

    private Color SelectionColor() => OwnerRoot?.Style?.Selection ?? UiStyle.DefaultSelection;

    /// <summary>
    /// How far to slide the text so the caret is inside the content. Only ever as far as it has to
    /// be, so a field that fits its text does not scroll at all and one that does not keeps the
    /// caret at whichever edge the typing is happening against.
    /// </summary>
    private int ScrollToShowCaret(Font font, string display, int caret, int contentWidth)
    {
        if (_editor == null || contentWidth <= 0)
        {
            return 0;
        }

        int caretX = TextWidth(font, display, caret);
        int offset = Math.Min(_scrollOffset, Math.Max(0, TextWidth(font, display) - contentWidth));

        if (caretX - offset > contentWidth)
        {
            offset = caretX - contentWidth;
        }
        else if (caretX - offset < 0)
        {
            offset = caretX;
        }

        return Math.Max(0, offset);
    }

    private static int TextWidth(Font font, string text, int length) => TextWidth(font, text[..length]);

    private static int TextWidth(Font font, string text) => text.Length == 0 ? 0 : font.CreateTextSprite(text).Size.X;

    private Font ResolveFont() =>
        _font
        ?? OwnerRoot?.Style?.Body
        ?? throw new InvalidOperationException(
            $"A {nameof(TextBox)} without an explicit Font needs a UiStyle with a Body font on the UiRoot it belongs to. " +
            "Set UiRoot.Style, or construct the field with a font.");
}
