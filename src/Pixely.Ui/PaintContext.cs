using System.Numerics;
using System.Runtime.InteropServices;
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
    private List<PaintInstruction> _instructions = new();
    private List<PaintInstruction> _completedInstructions = new();
    private readonly List<Rectangle> _clipStack = new();
    private int _generation;

    /// <summary>
    /// The last completed build, which is what the renderer paints. The working list is never
    /// exposed, so a build that throws partway leaves the previous frame in place.
    /// </summary>
    internal IReadOnlyList<PaintInstruction> Instructions => _completedInstructions;

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
    /// Promotes the working list to the completed one. Returns whether it differs from the build
    /// before it, which is what lets a rebuild that changed nothing visible skip the repaint.
    /// </summary>
    internal bool Complete()
    {
        bool changed = !SameInstructions(CollectionsMarshal.AsSpan(_instructions), CollectionsMarshal.AsSpan(_completedInstructions));
        (_instructions, _completedInstructions) = (_completedInstructions, _instructions);
        return changed;
    }

    // Textures are compared by reference, matching PaintBatcher, rather than through the generated
    // record equality, which would honour an Equals override on a Texture subclass.
    private static bool SameInstructions(ReadOnlySpan<PaintInstruction> a, ReadOnlySpan<PaintInstruction> b)
    {
        if (a.Length != b.Length)
        {
            return false;
        }

        for (int i = 0; i < a.Length; i++)
        {
            if (!ReferenceEquals(a[i].Texture, b[i].Texture) || a[i].Area != b[i].Area || a[i].Clip != b[i].Clip || a[i].Uvs != b[i].Uvs || a[i].Tint != b[i].Tint)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Restricts drawing to <paramref name="clip"/> intersected with the clip already in force,
    /// until the returned scope is disposed.
    /// </summary>
    public ClipScope PushClip(Rectangle clip)
    {
        _clipStack.Add(CurrentClip.Intersect(clip));
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

        Rectangle visible = instruction.Clip.Intersect(instruction.Area);
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
    /// Restores the clip depth when the returned scope is disposed, so an unbalanced custom
    /// drawable stays a local bug instead of corrupting the rest of the frame.
    /// </summary>
    internal IsolationScope Isolate() => new(this, _clipStack.Count);

    private void TruncateClipsTo(int depth)
    {
        while (_clipStack.Count > depth)
        {
            _clipStack.RemoveAt(_clipStack.Count - 1);
        }
    }

    /// <summary>
    /// Restores the clip depth it was taken at. A ref struct rather than a callback, because the
    /// paint traversal enters one of these twice per element and a delegate would allocate a
    /// closure over the element each time.
    /// </summary>
    internal readonly ref struct IsolationScope
    {
        private readonly PaintContext _context;
        private readonly int _depth;

        internal IsolationScope(PaintContext context, int depth)
        {
            _context = context;
            _depth = depth;
        }

        public void Dispose() => _context?.TruncateClipsTo(_depth);
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
