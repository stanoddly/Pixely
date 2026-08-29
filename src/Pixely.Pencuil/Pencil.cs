using System.Numerics;
using System.Runtime.InteropServices;
using Pixely.Gpu;
using Pixely.Input;
using Pixely.Sprites;
using Pixely.Text;

namespace Pixely.Pencuil;

public enum LayoutDirection
{
    None, Bottom, Top, Left, Right
}

internal readonly record struct HoverRectanglePatch(Rectangle Area, int InstructionIndex, Color Color, Color HoverColor);
internal readonly record struct HoverTexturePatch(Rectangle Area, int InstructionIndex, FColor Tint, FColor HoverTint);

public readonly struct DirectionDisposer : IDisposable
{
    private readonly Pencil _context;
    private readonly Vector2Int _previousPosition;
    private readonly Vector2Int _previousSize;
    private readonly LayoutDirection _previousLayoutDirection;

    internal DirectionDisposer(
        Pencil context,
        Vector2Int previousPosition,
        Vector2Int previousSize,
        LayoutDirection previousLayoutDirection)
    {
        _context = context;
        _previousPosition = previousPosition;
        _previousSize = previousSize;
        _previousLayoutDirection = previousLayoutDirection;
    }

    public void Dispose()
    {
        _context.CurrentDirection = _previousLayoutDirection;
        _context.CurrentPosition = _previousPosition;
        _context.CurrentSize = _previousSize;
    }
}

public readonly struct GapDisposer : IDisposable
{
    private readonly Pencil _context;
    private readonly int _previousGap;

    internal GapDisposer(Pencil context, int previousGap)
    {
        _context = context;
        _previousGap = previousGap;
    }

    public void Dispose() => _context.CurrentGap = _previousGap;
}

public class Pencil
{
    private readonly IFontSystem _fontSystem;
    private readonly IClipboardService _clipboardService;
    public GuiStyle Style { get; }
    internal int _depth = 0;

    internal List<ColoredRectangleInstruction> _coloredRectangleInstructions = new();
    internal List<TextureRegionInstruction> _textureRegionInstructions = new();

    private List<ColoredRectangleInstruction> _previousColoredRectangleInstructions = new();
    private List<TextureRegionInstruction> _previousTextureRegionInstructions = new();

    internal List<ColoredRectangleInstruction> CompletedColoredRectangleInstructions => _previousColoredRectangleInstructions;
    internal List<TextureRegionInstruction> CompletedTextureRegionInstructions => _previousTextureRegionInstructions;

    private readonly List<Rectangle> _hoverAreas = new();
    private readonly List<Rectangle> _clickTests = new();
    // Patch indices address their corresponding completed instruction buffer after CycleInstructions;
    // reset and rebuild the patches whenever the instruction buffers are rebuilt.
    private readonly List<HoverRectanglePatch> _hoverRectanglePatches = new();
    private readonly List<HoverTexturePatch> _hoverTexturePatches = new();

    internal int _viewportWidth;
    internal int _viewportHeight;

    public bool NeedsUpdate { get; internal set; } = true;
    public void Invalidate() => NeedsUpdate = true;

    // Set when completed instructions or their patched presentation changes; read and
    // cleared by the render phase to decide whether to redraw the retained texture.
    internal bool RenderDirty { get; set; }

    internal ShortSize ViewportSize => new ShortSize((ushort)_viewportWidth, (ushort)_viewportHeight);
    internal ShortSize CompletedInstructionViewportSize { get; private set; }

    internal void UpdateViewport(int width, int height)
    {
        if (_viewportWidth == width && _viewportHeight == height)
        {
            return;
        }

        _viewportWidth = width;
        _viewportHeight = height;
        Invalidate();
    }

    internal void UpdateCursor(Vector2Int? position)
    {
        bool cursorInWindow = position.HasValue;
        Vector2Int nextPosition = position.GetValueOrDefault();
        if (cursorInWindow != IsCursorInWindow || (cursorInWindow && nextPosition != CursorPosition))
        {
            foreach (Rectangle area in _hoverAreas)
            {
                bool wasHovered = IsCursorInWindow && area.Intersects(CursorPosition);
                bool hovered = cursorInWindow && area.Intersects(nextPosition);
                if (wasHovered != hovered)
                {
                    Invalidate();
                    break;
                }
            }

            foreach (HoverRectanglePatch patch in _hoverRectanglePatches)
            {
                bool wasHovered = IsCursorInWindow && patch.Area.Intersects(CursorPosition);
                bool hovered = cursorInWindow && patch.Area.Intersects(nextPosition);
                if (wasHovered != hovered)
                {
                    ColoredRectangleInstruction instruction = _previousColoredRectangleInstructions[patch.InstructionIndex];
                    _previousColoredRectangleInstructions[patch.InstructionIndex] = instruction with
                    {
                        Color = hovered ? patch.HoverColor : patch.Color
                    };
                    RenderDirty = true;
                }
            }

            foreach (HoverTexturePatch patch in _hoverTexturePatches)
            {
                bool wasHovered = IsCursorInWindow && patch.Area.Intersects(CursorPosition);
                bool hovered = cursorInWindow && patch.Area.Intersects(nextPosition);
                if (wasHovered != hovered)
                {
                    TextureRegionInstruction instruction = _previousTextureRegionInstructions[patch.InstructionIndex];
                    _previousTextureRegionInstructions[patch.InstructionIndex] = instruction with
                    {
                        Tint = hovered ? patch.HoverTint : patch.Tint
                    };
                    RenderDirty = true;
                }
            }
        }

        IsCursorInWindow = cursorInWindow;
        if (cursorInWindow)
        {
            CursorPosition = nextPosition;
        }
    }

    public LayoutDirection CurrentDirection { get; set; } = LayoutDirection.Bottom;
    public Vector2Int CurrentPosition { get; set; }
    public Vector2Int CurrentSize { get; set; }
    public Vector2Int CursorPosition { get; private set; }
    public bool IsCursorInWindow { get; private set; }
    public int CurrentGap { get; set; }

    internal bool CursorJustReleased { get; set; }

    public int? FocusedControlId { get; private set; }
    public bool HasFocus => FocusedControlId != null;
    internal bool FocusedControlSeenThisFrame;
    internal TextFieldEditingState? EditingState;
    private int? _pendingFocusedControlId;
    private TextFieldEditingState? _pendingEditingState;

    public Pencil(
        IFontSystem fontSystem,
        IClipboardService clipboardService,
        GuiStyle guiStyle)
    {
        _fontSystem = fontSystem;
        _clipboardService = clipboardService;
        Style = guiStyle;
    }

    internal void AddHoverArea(Rectangle area)
    {
        _hoverAreas.Add(area);
    }

    internal void AddClickTest(Rectangle test)
    {
        _clickTests.Add(test);
    }

    internal bool IsOverInteractiveArea(Vector2Int position)
    {
        foreach (Rectangle area in _clickTests)
        {
            if (area.Intersects(position))
            {
                return true;
            }
        }

        return false;
    }

    public void AddRectangle(Rectangle rectangle, Color color)
    {
        _coloredRectangleInstructions.Add(new ColoredRectangleInstruction(_depth++, rectangle, color));
    }

    internal void AddHoverRectangle(Rectangle rectangle, Color color, Rectangle hoverArea, Color hoverColor)
    {
        int instructionIndex = _coloredRectangleInstructions.Count;
        Color resolvedColor = IsCursorInWindow && hoverArea.Intersects(CursorPosition) ? hoverColor : color;
        _coloredRectangleInstructions.Add(new ColoredRectangleInstruction(_depth++, rectangle, resolvedColor));
        if (color != hoverColor)
        {
            _hoverRectanglePatches.Add(new HoverRectanglePatch(hoverArea, instructionIndex, color, hoverColor));
        }
    }

    public void AddTexture(Texture texture, Rectangle area, Vector4 uvs, FColor tint)
    {
        _textureRegionInstructions.Add(new TextureRegionInstruction(_depth++, texture, area, uvs, tint));
    }

    internal void AddHoverTexture(Texture texture, Rectangle area, Vector4 uvs, FColor tint, Rectangle hoverArea, FColor hoverTint)
    {
        int instructionIndex = _textureRegionInstructions.Count;
        FColor resolvedTint = IsCursorInWindow && hoverArea.Intersects(CursorPosition) ? hoverTint : tint;
        _textureRegionInstructions.Add(new TextureRegionInstruction(_depth++, texture, area, uvs, resolvedTint));
        if (tint != hoverTint)
        {
            _hoverTexturePatches.Add(new HoverTexturePatch(hoverArea, instructionIndex, tint, hoverTint));
        }
    }

    public Vector2Int DetermineNextPosition(Vector2Int size)
    {
        int gap = CurrentSize != default ? CurrentGap : 0;

        if (CurrentDirection == LayoutDirection.Bottom)
        {
            return new Vector2Int(CurrentPosition.X, CurrentPosition.Y + CurrentSize.Y + gap);
        }

        if (CurrentDirection == LayoutDirection.Top)
        {
            return new Vector2Int(CurrentPosition.X, CurrentPosition.Y - size.Y - gap);
        }

        if (CurrentDirection == LayoutDirection.Left)
        {
            return new Vector2Int(CurrentPosition.X - size.X - gap, CurrentPosition.Y);
        }

        if (CurrentDirection == LayoutDirection.Right)
        {
            return new Vector2Int(CurrentPosition.X + CurrentSize.X + gap, CurrentPosition.Y);
        }

        return new Vector2Int(CurrentPosition.X, CurrentPosition.Y);
    }

    public void MoveTo(int x, int y)
    {
        CurrentPosition = new Vector2Int(x, y);
    }

    public void MoveTo(Vector2Int position)
    {
        CurrentPosition = position;
    }

    public Vector2Int TopLeft => new Vector2Int(0, 0);
    public Vector2Int TopCenter => new Vector2Int(_viewportWidth / 2, 0);
    public Vector2Int TopRight => new Vector2Int(_viewportWidth, 0);
    public Vector2Int CenterLeft => new Vector2Int(0, _viewportHeight / 2);
    public Vector2Int Center => new Vector2Int(_viewportWidth / 2, _viewportHeight / 2);
    public Vector2Int CenterRight => new Vector2Int(_viewportWidth, _viewportHeight / 2);
    public Vector2Int BottomLeft => new Vector2Int(0, _viewportHeight);
    public Vector2Int BottomCenter => new Vector2Int(_viewportWidth / 2, _viewportHeight);
    public Vector2Int BottomRight => new Vector2Int(_viewportWidth, _viewportHeight);

    public DirectionDisposer WithDirection(LayoutDirection direction)
    {
        DirectionDisposer disposer = new DirectionDisposer(
            this,
            CurrentPosition,
            CurrentSize,
            CurrentDirection);

        CurrentDirection = direction;
        CurrentSize = default;

        return disposer;
    }

    public GapDisposer WithGap(int gap)
    {
        GapDisposer disposer = new GapDisposer(this, CurrentGap);
        CurrentGap = gap;
        return disposer;
    }

    public void Text(string text, Font font, Color color)
    {
        if (text.Length == 0)
        {
            return;
        }

        Text(_fontSystem.CreateTextSprite(text, font), color);
    }

    public void Text(TextSpriteAsset textSprite, Color color)
    {
        ArgumentNullException.ThrowIfNull(textSprite);
        if (textSprite.Size.X == 0 || textSprite.Size.Y == 0)
        {
            return;
        }

        this.Image(textSprite, color);
    }

    public Vector2Int MeasureText(string text, Font font)
    {
        if (text.Length == 0)
        {
            return default;
        }

        ShortSize size = _fontSystem.MeasureTextSprite(text, font);
        return new Vector2Int(size.Width, size.Height);
    }

    internal TextSpriteAsset CreateTextSprite(string text, Font font) => _fontSystem.CreateTextSprite(text, font);

    public bool IsFocused(int id) => FocusedControlId == id;

    internal void Focus(
        int id,
        string initialValue,
        IFormatProvider? formatProvider = null,
        TextFieldValidator? acceptsEdit = null,
        TextFieldValidator? canCommit = null)
    {
        FocusedControlId = id;
        FocusedControlSeenThisFrame = true;
        EditingState = new TextFieldEditingState(
            initialValue,
            formatProvider,
            acceptsEdit,
            canCommit);
        Invalidate();
    }

    internal void RequestFocus(
        int id,
        string initialValue,
        IFormatProvider? formatProvider,
        TextFieldValidator? acceptsEdit,
        TextFieldValidator? canCommit)
    {
        if (!HasFocus)
        {
            Focus(id, initialValue, formatProvider, acceptsEdit, canCommit);
            return;
        }

        if (IsFocused(id))
        {
            return;
        }

        _pendingFocusedControlId = id;
        _pendingEditingState = new TextFieldEditingState(
            initialValue,
            formatProvider,
            acceptsEdit,
            canCommit);
        Invalidate();
    }

    internal bool HasPendingFocus => _pendingFocusedControlId != null;

    internal void Blur()
    {
        FocusedControlId = _pendingFocusedControlId;
        EditingState = _pendingEditingState;
        _pendingFocusedControlId = null;
        _pendingEditingState = null;
        Invalidate();
    }

    internal void FinishBuild()
    {
        if (HasFocus && !FocusedControlSeenThisFrame)
        {
            Blur();
        }
    }

    internal void InsertText(string text)
    {
        if (EditingState == null)
        {
            return;
        }

        EditingState.TryInsertText(text);
        Invalidate();
    }

    internal bool HandleEditingKeyDown(Scancode scancode, bool shift, bool ctrl)
    {
        if (EditingState == null)
        {
            return false;
        }

        switch (scancode)
        {
            case Scancode.Backspace:
                if (EditingState.HasSelection)
                {
                    EditingState.TryDeleteSelection();
                }
                else if (ctrl)
                {
                    int target = FindWordBoundaryLeft(EditingState.Buffer, EditingState.CursorPosition);
                    EditingState.TryRemove(target, EditingState.CursorPosition - target);
                }
                else if (EditingState.CursorPosition > 0)
                {
                    EditingState.TryRemove(EditingState.CursorPosition - 1, 1);
                }
                break;
            case Scancode.Delete:
                if (EditingState.HasSelection)
                {
                    EditingState.TryDeleteSelection();
                }
                else if (ctrl)
                {
                    int target = FindWordBoundaryRight(EditingState.Buffer, EditingState.CursorPosition);
                    EditingState.TryRemove(EditingState.CursorPosition, target - EditingState.CursorPosition);
                }
                else if (EditingState.CursorPosition < EditingState.Buffer.Length)
                {
                    EditingState.TryRemove(EditingState.CursorPosition, 1);
                }
                break;
            case Scancode.Left:
                if (shift)
                {
                    EditingState.SelectionAnchor ??= EditingState.CursorPosition;
                    EditingState.CursorPosition = ctrl
                        ? FindWordBoundaryLeft(EditingState.Buffer, EditingState.CursorPosition)
                        : Math.Max(0, EditingState.CursorPosition - 1);
                }
                else if (EditingState.HasSelection && !ctrl)
                {
                    (int start, _) = EditingState.GetSelectionRange();
                    EditingState.CursorPosition = start;
                    EditingState.SelectionAnchor = null;
                }
                else
                {
                    EditingState.SelectionAnchor = null;
                    EditingState.CursorPosition = ctrl
                        ? FindWordBoundaryLeft(EditingState.Buffer, EditingState.CursorPosition)
                        : Math.Max(0, EditingState.CursorPosition - 1);
                }
                break;
            case Scancode.Right:
                if (shift)
                {
                    EditingState.SelectionAnchor ??= EditingState.CursorPosition;
                    EditingState.CursorPosition = ctrl
                        ? FindWordBoundaryRight(EditingState.Buffer, EditingState.CursorPosition)
                        : Math.Min(EditingState.Buffer.Length, EditingState.CursorPosition + 1);
                }
                else if (EditingState.HasSelection && !ctrl)
                {
                    (int start, int length) = EditingState.GetSelectionRange();
                    EditingState.CursorPosition = start + length;
                    EditingState.SelectionAnchor = null;
                }
                else
                {
                    EditingState.SelectionAnchor = null;
                    EditingState.CursorPosition = ctrl
                        ? FindWordBoundaryRight(EditingState.Buffer, EditingState.CursorPosition)
                        : Math.Min(EditingState.Buffer.Length, EditingState.CursorPosition + 1);
                }
                break;
            case Scancode.Home:
                if (shift)
                {
                    EditingState.SelectionAnchor ??= EditingState.CursorPosition;
                }
                else
                {
                    EditingState.SelectionAnchor = null;
                }
                EditingState.CursorPosition = 0;
                break;
            case Scancode.End:
                if (shift)
                {
                    EditingState.SelectionAnchor ??= EditingState.CursorPosition;
                }
                else
                {
                    EditingState.SelectionAnchor = null;
                }
                EditingState.CursorPosition = EditingState.Buffer.Length;
                break;
            case Scancode.A:
                if (ctrl)
                {
                    EditingState.SelectionAnchor = 0;
                    EditingState.CursorPosition = EditingState.Buffer.Length;
                }
                else
                {
                    return false;
                }
                break;
            case Scancode.C:
                if (ctrl && EditingState.HasSelection)
                {
                    _clipboardService.SetText(EditingState.GetSelectedText());
                }
                else if (!ctrl)
                {
                    return false;
                }
                break;
            case Scancode.X:
                if (ctrl && EditingState.HasSelection)
                {
                    string selectedText = EditingState.GetSelectedText();
                    if (EditingState.TryDeleteSelection())
                    {
                        _clipboardService.SetText(selectedText);
                    }
                }
                else if (!ctrl)
                {
                    return false;
                }
                break;
            case Scancode.V:
                if (ctrl)
                {
                    string? clipboardText = _clipboardService.GetText();
                    if (clipboardText != null)
                    {
                        EditingState.TryInsertText(clipboardText);
                    }
                }
                else
                {
                    return false;
                }
                break;
            case Scancode.Return:
            case Scancode.Return2:
            case Scancode.KeypadEnter:
                EditingState.Committed = true;
                break;
            case Scancode.Escape:
                EditingState.Canceled = true;
                break;
            default:
                return false;
        }

        Invalidate();
        return true;
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

    internal bool HaveInstructionsChanged()
    {
        return
            !CollectionsMarshal.AsSpan(_coloredRectangleInstructions).SequenceEqual(CollectionsMarshal.AsSpan(_previousColoredRectangleInstructions)) ||
            !CollectionsMarshal.AsSpan(_textureRegionInstructions).SequenceEqual(CollectionsMarshal.AsSpan(_previousTextureRegionInstructions));
    }

    // Interaction data is populated during a build pass and read by input handlers until
    // the next build, so it must be cleared before new instructions are produced
    internal void ResetInteractionData()
    {
        _hoverAreas.Clear();
        _clickTests.Clear();
        _hoverRectanglePatches.Clear();
        _hoverTexturePatches.Clear();
    }

    internal void MarkInstructionsCompleted()
    {
        CompletedInstructionViewportSize = ViewportSize;
    }

    internal void CycleInstructions()
    {
        (_coloredRectangleInstructions, _previousColoredRectangleInstructions) =
            (_previousColoredRectangleInstructions, _coloredRectangleInstructions);
        (_textureRegionInstructions, _previousTextureRegionInstructions) =
            (_previousTextureRegionInstructions, _textureRegionInstructions);

        _coloredRectangleInstructions.Clear();
        _textureRegionInstructions.Clear();
        _depth = 0;
    }
}

public static class PencilExtensions
{
    public static void Image(this Pencil pencil, SpriteAsset sprite, Color tint)
    {
        Vector2Int size = new Vector2Int(sprite.Size.X, sprite.Size.Y);
        Vector2Int position = pencil.CurrentPosition;
        Rectangle area = new Rectangle(position, size);
        pencil.AddTexture(sprite.Texture, area, sprite.CalculateTextureRegionUVs(), (FColor)tint);
        pencil.CurrentSize = size;
        pencil.CurrentPosition = pencil.DetermineNextPosition(size);
    }

    public static void Image(this Pencil pencil, SpriteAsset sprite, int width, int height, Color tint)
    {
        Vector2Int size = new Vector2Int(width, height);
        Vector2Int position = pencil.CurrentPosition;
        Rectangle area = new Rectangle(position, size);
        pencil.AddTexture(sprite.Texture, area, sprite.CalculateTextureRegionUVs(), (FColor)tint);
        pencil.CurrentSize = size;
        pencil.CurrentPosition = pencil.DetermineNextPosition(size);
    }

    public static void Rectangle(this Pencil pencil, int width, int height, Color color)
    {
        Rectangle area = PlaceElement(pencil, width, height);
        pencil.AddRectangle(area, color);
    }

    public static void HoverRectangle(this Pencil pencil, int width, int height, Color color, Color hoverColor)
    {
        Rectangle area = PlaceElement(pencil, width, height);
        pencil.AddHoverRectangle(area, color, area, hoverColor);
    }

    public static bool ClickArea(this Pencil pencil, int width, int height, bool enabled = true)
    {
        Rectangle area = PlaceElement(pencil, width, height);
        return ClickArea(pencil, area, enabled);
    }

    public static bool ClickArea(this Pencil pencil, Rectangle area, bool enabled = true)
    {
        if (!enabled)
        {
            return false;
        }

        pencil.AddClickTest(area);
        return pencil.CursorJustReleased && pencil.IsCursorInWindow && area.Intersects(pencil.CursorPosition);
    }

    public static bool HoverArea(this Pencil pencil, int width, int height, bool enabled = true)
    {
        Rectangle area = PlaceElement(pencil, width, height);
        return HoverArea(pencil, area, enabled);
    }

    public static bool HoverArea(this Pencil pencil, Rectangle area, bool enabled = true)
    {
        if (!enabled)
        {
            return false;
        }

        pencil.AddHoverArea(area);
        return pencil.IsCursorInWindow && area.Intersects(pencil.CursorPosition);
    }

    public static bool Button(this Pencil pencil, string text, Font font)
    {
        return Button(pencil, text, font, enabled: true);
    }

    public static bool Button(this Pencil pencil, string text, Font font, bool enabled)
    {
        TextSpriteAsset? textSprite = text.Length == 0 ? null : pencil.CreateTextSprite(text, font);
        Vector2Int textSize = GetTextSize(textSprite);
        int width = textSize.X + pencil.Style.TextPadding * 2;
        int height = textSize.Y + pencil.Style.TextPadding * 2;
        return ButtonCore(pencil, textSprite, width, height, enabled);
    }

    public static bool Button(this Pencil pencil, string text, Font font, int width, int height, bool enabled = true)
    {
        TextSpriteAsset? textSprite = text.Length == 0 ? null : pencil.CreateTextSprite(text, font);
        return ButtonCore(pencil, textSprite, width, height, enabled);
    }

    public static bool Button(this Pencil pencil, TextSpriteAsset textSprite)
    {
        return Button(pencil, textSprite, enabled: true);
    }

    public static bool Button(this Pencil pencil, TextSpriteAsset textSprite, bool enabled)
    {
        ArgumentNullException.ThrowIfNull(textSprite);

        Vector2Int textSize = GetTextSize(textSprite);
        int width = textSize.X + pencil.Style.TextPadding * 2;
        int height = textSize.Y + pencil.Style.TextPadding * 2;
        return Button(pencil, textSprite, width, height, enabled);
    }

    public static bool Button(this Pencil pencil, TextSpriteAsset textSprite, int width, int height, bool enabled = true)
    {
        ArgumentNullException.ThrowIfNull(textSprite);
        return ButtonCore(pencil, textSprite, width, height, enabled);
    }

    private static bool ButtonCore(Pencil pencil, TextSpriteAsset? textSprite, int width, int height, bool enabled)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        GuiStyle style = pencil.Style;
        Rectangle area = PlaceElement(pencil, width, height);
        bool clicked = ClickArea(pencil, area, enabled);
        Color backgroundColor = style.Background;
        Color textColor = enabled ? style.TextColor : style.InactiveColor;

        if (style.BorderThickness > 0)
        {
            pencil.AddRectangle(area, style.InactiveColor);
            int innerWidth = area.Width - style.BorderThickness * 2;
            int innerHeight = area.Height - style.BorderThickness * 2;
            if (innerWidth > 0 && innerHeight > 0)
            {
                Rectangle backgroundArea = new Rectangle(area.X + style.BorderThickness, area.Y + style.BorderThickness, innerWidth, innerHeight);
                if (enabled)
                {
                    pencil.AddHoverRectangle(backgroundArea, backgroundColor, area, style.ActiveColor);
                }
                else
                {
                    pencil.AddRectangle(backgroundArea, backgroundColor);
                }
            }
        }
        else if (enabled)
        {
            pencil.AddHoverRectangle(area, backgroundColor, area, style.ActiveColor);
        }
        else
        {
            pencil.AddRectangle(area, backgroundColor);
        }

        if (textSprite != null)
        {
            Vector2Int textSize = GetTextSize(textSprite);
            Rectangle textArea = new Rectangle(area.X + (area.Width - textSize.X) / 2, area.Y + (area.Height - textSize.Y) / 2, textSize.X, textSize.Y);
            if (enabled)
            {
                pencil.AddHoverTexture(textSprite.Texture, textArea, textSprite.CalculateTextureRegionUVs(), (FColor)textColor, area, (FColor)style.ActiveTextColor);
            }
            else
            {
                pencil.AddTexture(textSprite.Texture, textArea, textSprite.CalculateTextureRegionUVs(), (FColor)textColor);
            }
        }

        return clicked;
    }

    private static Vector2Int GetTextSize(TextSpriteAsset? textSprite) => textSprite == null ? default : new Vector2Int(textSprite.Size.X, textSprite.Size.Y);

    private static Rectangle PlaceElement(Pencil pencil, int width, int height)
    {
        Vector2Int size = new Vector2Int(width, height);
        Rectangle area = new Rectangle(pencil.CurrentPosition, size);
        pencil.CurrentSize = size;
        pencil.CurrentPosition = pencil.DetermineNextPosition(size);
        return area;
    }

    public static bool TextField(this Pencil pencil, int id, ref string value, Font font, int width)
    {
        bool committed = TextField(
            pencil,
            id,
            value,
            font,
            width,
            null,
            null,
            null,
            out string committedValue);

        if (committed)
        {
            value = committedValue;
        }

        return committed;
    }

    public static bool NumberField<T>(
        this Pencil pencil,
        int id,
        ref T value,
        Font font,
        int width,
        IFormatProvider? formatProvider = null)
        where T : struct, INumber<T>
    {
        string formattedValue = value.ToString(null, formatProvider);
        bool committed = TextField(
            pencil,
            id,
            formattedValue,
            font,
            width,
            formatProvider,
            NumberFieldValidators<T>.AcceptsEdit,
            NumberFieldValidators<T>.CanCommit,
            out string committedValue);

        if (!committed || !TryParseFiniteNumber(committedValue, formatProvider, out T parsedValue))
        {
            return false;
        }

        value = parsedValue;
        return true;
    }

    private static bool TextField(
        Pencil pencil,
        int id,
        string value,
        Font font,
        int width,
        IFormatProvider? formatProvider,
        TextFieldValidator? acceptsEdit,
        TextFieldValidator? canCommit,
        out string committedValue)
    {
        GuiStyle style = pencil.Style;
        int padding = style.TextPadding;
        Vector2Int textSize = pencil.MeasureText("Ay", font);
        int height = textSize.Y + padding * 2;
        Vector2Int size = new Vector2Int(width, height);
        Vector2Int position = pencil.CurrentPosition;
        Rectangle area = new Rectangle(position, size);
        bool clicked = ClickArea(pencil, area);

        bool isFocused = pencil.IsFocused(id);
        bool committed = false;
        committedValue = value;

        if (isFocused && pencil.EditingState != null)
        {
            pencil.FocusedControlSeenThisFrame = true;
            bool focusLost =
                pencil.HasPendingFocus ||
                (pencil.CursorJustReleased && (!pencil.IsCursorInWindow || !area.Intersects(pencil.CursorPosition)));

            if (pencil.EditingState.Canceled)
            {
                pencil.Blur();
                isFocused = false;
            }
            else if (pencil.EditingState.Committed || focusLost)
            {
                if (pencil.EditingState.CanCommit())
                {
                    committedValue = pencil.EditingState.Buffer;
                    committed = true;
                    pencil.Blur();
                    isFocused = false;
                }
                else if (focusLost)
                {
                    pencil.Blur();
                    isFocused = false;
                }
                else
                {
                    pencil.EditingState.Committed = false;
                }
            }
        }

        if (clicked)
        {
            if (!isFocused)
            {
                pencil.RequestFocus(
                    id,
                    value,
                    formatProvider,
                    acceptsEdit,
                    canCommit);
                isFocused = pencil.IsFocused(id);
            }
        }

        Color bgColor = isFocused ? style.ActiveColor : style.Background;
        pencil.AddRectangle(area, bgColor);

        string displayText = isFocused && pencil.EditingState != null
            ? pencil.EditingState.Buffer
            : value;

        Vector2Int textPosition = new Vector2Int(position.X + padding, position.Y + padding);

        if (isFocused && pencil.EditingState != null && pencil.EditingState.HasSelection)
        {
            (int selStart, int selLength) = pencil.EditingState.GetSelectionRange();
            int selStartX = textPosition.X;
            if (selStart > 0)
            {
                Vector2Int beforeSelSize = pencil.MeasureText(displayText[..selStart], font);
                selStartX += beforeSelSize.X;
            }
            Vector2Int selTextSize = pencil.MeasureText(displayText.Substring(selStart, selLength), font);
            Rectangle selRect = new Rectangle(selStartX, position.Y + padding, selTextSize.X, textSize.Y);
            pencil.AddRectangle(selRect, style.SelectionColor);
        }

        if (displayText.Length > 0)
        {
            Vector2Int savedPosition = pencil.CurrentPosition;
            pencil.CurrentPosition = textPosition;
            pencil.Text(displayText, font, style.TextColor);
            pencil.CurrentPosition = savedPosition;
        }

        if (isFocused && pencil.EditingState != null)
        {
            int cursorX;
            if (pencil.EditingState.CursorPosition > 0 && displayText.Length > 0)
            {
                string beforeCursor = displayText[..pencil.EditingState.CursorPosition];
                Vector2Int beforeSize = pencil.MeasureText(beforeCursor, font);
                cursorX = textPosition.X + beforeSize.X;
            }
            else
            {
                cursorX = textPosition.X;
            }

            Rectangle cursorRect = new Rectangle(cursorX, position.Y + padding, 1, textSize.Y);
            pencil.AddRectangle(cursorRect, style.TextColor);
        }

        pencil.CurrentSize = size;
        pencil.CurrentPosition = pencil.DetermineNextPosition(size);

        return committed;
    }

    private static bool CanEditNumber<T>(string text, IFormatProvider? formatProvider)
        where T : struct, INumber<T>
    {
        return
            text.Length == 0 ||
            TryParseFiniteNumber(text, formatProvider, out T _) ||
            TryParseFiniteNumber(text + "0", formatProvider, out T _);
    }

    private static bool TryParseFiniteNumber<T>(
        string text,
        IFormatProvider? formatProvider,
        out T value)
        where T : struct, INumber<T>
    {
        return T.TryParse(text, formatProvider, out value) && T.IsFinite(value);
    }

    private static class NumberFieldValidators<T>
        where T : struct, INumber<T>
    {
        internal static readonly TextFieldValidator AcceptsEdit =
            static (text, formatProvider) => CanEditNumber<T>(text, formatProvider);

        internal static readonly TextFieldValidator CanCommit =
            static (text, formatProvider) =>
                TryParseFiniteNumber(text, formatProvider, out T _);
    }
}

internal delegate bool TextFieldValidator(string text, IFormatProvider? formatProvider);

internal class TextFieldEditingState
{
    private readonly IFormatProvider? _formatProvider;
    private readonly TextFieldValidator? _acceptsEdit;
    private readonly TextFieldValidator? _canCommit;

    public string Buffer;
    public int CursorPosition;
    public int? SelectionAnchor;
    public bool Committed;
    public bool Canceled;

    public TextFieldEditingState(
        string initialValue,
        IFormatProvider? formatProvider = null,
        TextFieldValidator? acceptsEdit = null,
        TextFieldValidator? canCommit = null)
    {
        _formatProvider = formatProvider;
        _acceptsEdit = acceptsEdit;
        _canCommit = canCommit;
        Buffer = initialValue;
        CursorPosition = initialValue.Length;
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
        if (length == 0)
        {
            return string.Empty;
        }

        return Buffer.Substring(start, length);
    }

    public bool TryInsertText(string text)
    {
        (int start, int length) = GetSelectionRange();
        return TryReplace(start, length, text);
    }

    public bool TryDeleteSelection()
    {
        (int start, int length) = GetSelectionRange();
        if (length == 0)
        {
            return false;
        }

        return TryReplace(start, length, string.Empty);
    }

    public bool TryRemove(int start, int length)
    {
        return TryReplace(start, length, string.Empty);
    }

    public bool CanCommit()
    {
        return _canCommit == null || _canCommit(Buffer, _formatProvider);
    }

    private bool TryReplace(int start, int length, string replacement)
    {
        string candidate = Buffer.Remove(start, length).Insert(start, replacement);
        if (_acceptsEdit != null && !_acceptsEdit(candidate, _formatProvider))
        {
            return false;
        }

        Buffer = candidate;
        CursorPosition = start + replacement.Length;
        SelectionAnchor = null;
        return true;
    }
}
