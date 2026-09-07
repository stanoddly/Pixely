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
    private const int CaretWidth = 1;

    private readonly IFont? _font;

    private string _text = string.Empty;
    private TextEditingBuffer? _editor;

    // How far the text is slid left so the caret stays in view. Paint owns it: it is the only place
    // that knows how wide anything is.
    private int _scrollOffset;

    public TextBox(IFont? font = null)
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

    /// <summary>
    /// The clipboard the copy, cut and paste shortcuts reach. Null leaves them inert: cut still
    /// deletes, because that is the field's own business, but nothing is copied or pasted.
    /// </summary>
    public IClipboardService? Clipboard { get; set; }

    /// <summary>What is on screen: the edit while there is one, and the value otherwise.</summary>
    public string DisplayText => _editor?.Text ?? _text;

    /// <summary>Whether an edit is in progress.</summary>
    public bool IsEditing => _editor != null;

    /// <summary>
    /// Focused rather than hovered: a field shows which one the typing goes to, and a pointer resting
    /// over it says nothing about that.
    /// </summary>
    public VisualState VisualState
    {
        get
        {
            if (!IsEffectivelyEnabled)
            {
                return VisualState.Disabled;
            }

            return _editor != null ? VisualState.Focused : VisualState.Normal;
        }
    }

    // A plain Background assigned through the inherited property means one look for every state.
    // Honouring it is what keeps that property from accepting a value and then quietly doing
    // nothing on this one element.
    protected override Drawable? EffectiveBackground =>
        base.EffectiveBackground ?? Style.Field.Background.Resolve(VisualState);

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
        IFont font = ResolveFont();

        // Measured against the value rather than what is being typed, so a field does not resize
        // itself with every keystroke and shuffle everything beside it along.
        int height = font.Measure("Ay").Height;
        int width = _text.Length == 0 ? 0 : font.Measure(_text).Width;
        return new Vector2Int(width, height);
    }

    protected override void PaintContent(PaintContext context)
    {
        IFont font = ResolveFont();
        Rectangle content = Padding.Deflate(Bounds);
        string display = DisplayText;
        int caret = _editor?.CursorPosition ?? 0;

        _scrollOffset = _editor == null
            ? 0
            : ScrollOffsetFor(TextWidth(font, display, caret), TextWidth(font, display), content.Width, _scrollOffset);
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
            context.DrawSprite(sprite, new Rectangle(originX, content.Y, sprite.Size.X, sprite.Size.Y), ForegroundColor());
        }

        if (_editor != null)
        {
            context.FillRectangle(new Rectangle(originX + TextWidth(font, display, caret), content.Y, CaretWidth, content.Height), CaretColor());
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
            // Focus is already going; asking for it to go again would be answered by this same
            // callback, which by then has nothing left to commit.
            Commit(releaseFocus: false);
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
                    Commit(releaseFocus: true);
                }

                break;

            case TextEditingOutcome.Cancel:
                UiRoot? root = OwnerRoot;
                Discard();
                root?.Focus(null);
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

    private void Commit(bool releaseFocus)
    {
        string value = _editor!.Text;

        // Read before any callback runs: one of them may move this field to another root, and the
        // focus being given up belongs to the root it is being given up on.
        UiRoot? root = OwnerRoot;

        // Cleared before anything is raised, so a handler that assigns to this field or moves focus
        // out of it is not fighting an edit that is still notionally in progress.
        _editor = null;
        SetMeasureProperty(ref _text, value);

        // The caret was on screen a moment ago whether or not the value changed, so this cannot wait
        // for the value to differ.
        InvalidatePaint();

        // Before the notification rather than after it. A handler is free to move focus to the next
        // field, and releasing afterwards would take it straight back off again; and a handler that
        // throws would otherwise leave this field focused with no edit, refusing every key.
        if (releaseFocus)
        {
            root?.Focus(null);
        }

        OnCommitted(value);
        Committed?.Invoke(value);
    }

    private void Discard()
    {
        _editor = null;
        InvalidatePaint();
    }

    private Color SelectionColor() => Style.Field.Selection;

    private Color ForegroundColor() => IsEffectivelyEnabled ? Style.Field.Foreground : Style.Field.Disabled;

    private Color CaretColor() => Style.Field.Caret ?? ForegroundColor();

    /// <summary>
    /// How far to slide the text left so the caret is inside the content. Only ever as far as it has
    /// to be, so a field that fits its text does not scroll at all and one that does not keeps the
    /// caret at whichever edge the typing is happening against.
    /// </summary>
    /// <remarks>
    /// Takes widths rather than text so the arithmetic can be checked on its own. Everything else
    /// about drawing a field needs a font that can rasterise.
    /// </remarks>
    internal static int ScrollOffsetFor(int caretX, int textWidth, int contentWidth, int currentOffset)
    {
        if (contentWidth <= 0)
        {
            return 0;
        }

        // Never further than the text reaches: a deletion can leave an offset that would otherwise
        // scroll past the end and show empty space.
        int offset = Math.Min(currentOffset, Math.Max(0, textWidth - contentWidth));

        // The caret is a rectangle starting at its own coordinate, so sitting exactly on the trailing
        // edge puts it wholly outside a clip that ends there.
        if (caretX - offset + CaretWidth > contentWidth)
        {
            offset = caretX + CaretWidth - contentWidth;
        }
        else if (caretX - offset < 0)
        {
            offset = caretX;
        }

        return Math.Max(0, offset);
    }

    private static int TextWidth(IFont font, string text, int length) => TextWidth(font, text[..length]);

    private static int TextWidth(IFont font, string text) => text.Length == 0 ? 0 : font.Measure(text).Width;

    private IFont ResolveFont() =>
        _font
        ?? Style.Body
        ?? throw new InvalidOperationException(
            $"A {nameof(TextBox)} without an explicit Font needs a UiStyle with a Body font on the UiRoot it belongs to. " +
            "Set UiRoot.Style, or construct the field with a font.");
}
