using System.Numerics;
using Pixely.Gpu;
using Pixely.Sprites;

namespace Pixely.Ui;

/// <summary>
/// Collects the quads for one frame. Custom <see cref="Drawable"/> implementations draw through
/// this, so its emit methods are public — but there is deliberately no public pop: a clip is
/// released only by disposing the <see cref="ClipScope"/> that pushed it.
/// </summary>
public sealed class PaintContext
{
    private readonly List<PaintInstruction> _instructions = new();
    private readonly List<Rectangle> _clipStack = new();
    private int _generation;

    internal IReadOnlyList<PaintInstruction> Instructions => _instructions;

    /// <summary>The clip every emitted quad is currently restricted to.</summary>
    public Rectangle CurrentClip => _clipStack.Count == 0 ? default : _clipStack[^1];

    internal int ClipDepth => _clipStack.Count;

    internal void Reset(Rectangle viewport)
    {
        _instructions.Clear();
        _clipStack.Clear();
        _clipStack.Add(viewport);
        _generation++;
    }

    /// <summary>
    /// Restricts drawing to <paramref name="clip"/> intersected with the clip already in force,
    /// until the returned scope is disposed.
    /// </summary>
    public ClipScope PushClip(Rectangle clip)
    {
        _clipStack.Add(Intersect(CurrentClip, clip));
        return new ClipScope(this, _clipStack.Count, _generation);
    }

    public void FillRectangle(Rectangle area, Color color) => FillRectangle(area, (FColor)color);

    public void FillRectangle(Rectangle area, FColor color)
    {
        Emit(new PaintInstruction(area, CurrentClip, null, PaintInstruction.FullUvs, color));
    }

    public void DrawSprite(SpriteAsset sprite, Rectangle area, Color tint) => DrawSprite(sprite, area, (FColor)tint);

    public void DrawSprite(SpriteAsset sprite, Rectangle area, FColor tint)
    {
        ArgumentNullException.ThrowIfNull(sprite);
        Emit(new PaintInstruction(area, CurrentClip, sprite.Texture, sprite.CalculateTextureRegionUVs(), tint));
    }

    public void DrawTexture(Texture texture, Rectangle area, Vector4 uvs, FColor tint)
    {
        ArgumentNullException.ThrowIfNull(texture);
        Emit(new PaintInstruction(area, CurrentClip, texture, uvs, tint));
    }

    private void Emit(PaintInstruction instruction)
    {
        // A quad with nothing visible costs a draw call and can produce an empty or negative
        // scissor, so it is dropped here rather than defended against in the renderer.
        if (instruction.Area.Width <= 0 || instruction.Area.Height <= 0)
        {
            return;
        }

        Rectangle visible = Intersect(instruction.Clip, instruction.Area);
        if (visible.Width <= 0 || visible.Height <= 0)
        {
            return;
        }

        _instructions.Add(instruction);
    }

    /// <summary>
    /// Restores the clip stack to <paramref name="depth"/>. Only <see cref="ClipScope.Dispose"/>
    /// calls this, and only when it still owns the top of the stack.
    /// </summary>
    internal void PopClipTo(int depth, int generation)
    {
        if (generation != _generation || _clipStack.Count != depth)
        {
            return;
        }

        _clipStack.RemoveAt(_clipStack.Count - 1);
    }

    /// <summary>
    /// Runs <paramref name="paint"/> and restores the clip depth afterwards, so an unbalanced
    /// custom drawable stays a local bug instead of corrupting the rest of the frame.
    /// </summary>
    internal void PaintIsolated(Action paint)
    {
        int depth = _clipStack.Count;
        paint();

        while (_clipStack.Count > depth)
        {
            _clipStack.RemoveAt(_clipStack.Count - 1);
        }
    }

    internal static Rectangle Intersect(Rectangle first, Rectangle second)
    {
        int left = Math.Max(first.X, second.X);
        int top = Math.Max(first.Y, second.Y);
        int right = Math.Min(first.X + first.Width, second.X + second.Width);
        int bottom = Math.Min(first.Y + first.Height, second.Y + second.Height);

        return new Rectangle(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }
}

/// <summary>
/// Holds a pushed clip. Carries the stack depth it was pushed at and the frame it belongs to, so
/// a copied or already-disposed scope cannot pop a clip that is not its own.
/// </summary>
public readonly ref struct ClipScope
{
    private readonly PaintContext _context;
    private readonly int _depth;
    private readonly int _generation;

    internal ClipScope(PaintContext context, int depth, int generation)
    {
        _context = context;
        _depth = depth;
        _generation = generation;
    }

    public void Dispose() => _context?.PopClipTo(_depth, _generation);
}
