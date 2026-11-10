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
/// <remarks>
/// Everything passed in is in logical pixels. The quads are kept in raster pixels, the pixels of
/// the texture the renderer paints them into: logical ones under <see cref="UiScaleMode.Pixelated"/>,
/// and target ones under <see cref="UiScaleMode.Smooth"/>, where every edge is rounded to the
/// nearest target pixel. Rounding edges rather than sizes is what keeps two rectangles that meet in
/// logical pixels meeting in target ones.
/// </remarks>
public sealed class PaintContext
{
    private List<PaintInstruction> _instructions = new();
    private List<PaintInstruction> _completedInstructions = new();
    private readonly List<Rectangle> _clipStack = new();
    private readonly List<Rectangle> _rasterClipStack = new();
    private readonly List<(Rectangle Logical, Rectangle Raster)> _snaps = new();
    private int _generation;

    /// <summary>
    /// The last completed build, which is what the renderer paints. The working list is never
    /// exposed, so a build that throws partway leaves the previous frame in place.
    /// </summary>
    internal IReadOnlyList<PaintInstruction> Instructions => _completedInstructions;

    /// <summary>The clip every emitted quad is currently restricted to, in logical pixels.</summary>
    public Rectangle CurrentClip => _clipStack.Count == 0 ? default : _clipStack[^1];

    internal Rectangle CurrentRasterClip => _rasterClipStack.Count == 0 ? default : _rasterClipStack[^1];

    internal int ClipDepth => _clipStack.Count;

    /// <summary>How many raster pixels one logical pixel covers: 1 unless the root scales smoothly.</summary>
    internal float Scale { get; private set; } = 1f;

    /// <summary>The filter a sprite is sampled with when its element does not choose one.</summary>
    internal TextureFilter Filter { get; private set; } = TextureFilter.Nearest;

    internal void Reset(Rectangle viewport) => Reset(viewport, 1f, new Vector2Int(viewport.Width, viewport.Height), TextureFilter.Nearest);

    /// <param name="viewport">The logical rectangle the tree is laid out in.</param>
    /// <param name="scale">How many raster pixels one logical pixel covers.</param>
    /// <param name="rasterSize">The size of the texture the quads are painted into, which bounds every clip.</param>
    /// <param name="filter">The filter sprites are sampled with unless their element chooses one.</param>
    internal void Reset(Rectangle viewport, float scale, Vector2Int rasterSize, TextureFilter filter)
    {
        _instructions.Clear();
        _clipStack.Clear();
        _rasterClipStack.Clear();
        _snaps.Clear();
        _generation++;
        Scale = scale;
        Filter = filter;
        _clipStack.Add(viewport);

        // The viewport is rounded up, so at a scale it can reach past the target, and a scissor
        // outside the texture it applies to is not valid on every backend.
        _rasterClipStack.Add(ToRaster(viewport).Intersect(new Rectangle(0, 0, rasterSize.X, rasterSize.Y)));
    }

    /// <summary>
    /// Promotes the working list to the completed one. Returns whether it differs from the build
    /// before it, which is what lets a rebuild that changed nothing visible skip the repaint.
    /// </summary>
    internal bool Complete()
    {
        bool changed = !CollectionsMarshal.AsSpan(_instructions).SequenceEqual(CollectionsMarshal.AsSpan(_completedInstructions));
        (_instructions, _completedInstructions) = (_completedInstructions, _instructions);
        return changed;
    }

    /// <summary>
    /// Restricts drawing to <paramref name="clip"/> intersected with the clip already in force,
    /// until the returned scope is disposed.
    /// </summary>
    public ClipScope PushClip(Rectangle clip)
    {
        _clipStack.Add(CurrentClip.Intersect(clip));
        _rasterClipStack.Add(CurrentRasterClip.Intersect(ToRaster(clip)));
        return new ClipScope(this, _clipStack.Count, _generation);
    }

    public void FillRectangle(Rectangle area, SrgbColor color) => FillRectangle(area, (LinearColor)color);

    public void FillRectangle(Rectangle area, LinearColor color) => FillRaster(ToRaster(area), color);

    public void DrawSprite(SpriteAsset sprite, Rectangle area, SrgbColor tint) => DrawSprite(sprite, area, (LinearColor)tint);

    /// <summary>Stretches <paramref name="sprite"/> over <paramref name="area"/>, sampled with the root's filter.</summary>
    public void DrawSprite(SpriteAsset sprite, Rectangle area, LinearColor tint) => DrawSprite(sprite, area, tint, Filter);

    /// <summary>
    /// Stretches <paramref name="sprite"/> over <paramref name="area"/>, sampled with <paramref name="filter"/>. Linear
    /// sampling averages every texel a pixel covers, so a sprite shown much smaller than its texture does not alias.
    /// </summary>
    public void DrawSprite(SpriteAsset sprite, Rectangle area, LinearColor tint, TextureFilter filter)
    {
        ArgumentNullException.ThrowIfNull(sprite);
        DrawRaster(sprite.Texture, ToRaster(area), sprite.CalculateTextureRegionUVs(), tint, filter);
    }

    public void DrawTexture(Texture texture, Rectangle area, Vector4 uvs, LinearColor tint) => DrawTexture(texture, area, uvs, tint, Filter);

    /// <inheritdoc cref="DrawSprite(SpriteAsset, Rectangle, LinearColor, TextureFilter)"/>
    public void DrawTexture(Texture texture, Rectangle area, Vector4 uvs, LinearColor tint, TextureFilter filter)
    {
        ArgumentNullException.ThrowIfNull(texture);
        DrawRaster(texture, ToRaster(area), uvs, tint, filter);
    }

    /// <summary>Fills a rectangle already in raster pixels.</summary>
    internal void FillRaster(Rectangle area, LinearColor color) => Emit(area, null, PaintInstruction.FullUvs, color, TextureFilter.Nearest);

    /// <summary>Draws a texture over a rectangle already in raster pixels.</summary>
    internal void DrawRaster(Texture texture, Rectangle area, Vector4 uvs, LinearColor tint, TextureFilter filter) => Emit(area, texture, uvs, tint, filter);

    /// <summary>
    /// The raster pixel a logical coordinate on the x axis lands on. Rounded half up, which unlike
    /// rounding half to even moves with the coordinate: a sprite one whole raster pixel wide keeps
    /// its width wherever it starts.
    /// </summary>
    internal int ToRasterX(int x)
    {
        for (int i = _snaps.Count - 1; i >= 0; i--)
        {
            (Rectangle logical, Rectangle raster) = _snaps[i];

            if (x == logical.X)
            {
                return raster.X;
            }

            if (x == logical.X + logical.Width)
            {
                return raster.X + raster.Width;
            }
        }

        return Round(x);
    }

    /// <inheritdoc cref="ToRasterX"/>
    internal int ToRasterY(int y)
    {
        for (int i = _snaps.Count - 1; i >= 0; i--)
        {
            (Rectangle logical, Rectangle raster) = _snaps[i];

            if (y == logical.Y)
            {
                return raster.Y;
            }

            if (y == logical.Y + logical.Height)
            {
                return raster.Y + raster.Height;
            }
        }

        return Round(y);
    }

    /// <summary>A logical rectangle in raster pixels, by rounding each of its edges.</summary>
    internal Rectangle ToRaster(Rectangle area)
    {
        if (Scale == 1f && _snaps.Count == 0)
        {
            return area;
        }

        int left = ToRasterX(area.X);
        int top = ToRasterY(area.Y);
        return new Rectangle(left, top, ToRasterX(area.X + area.Width) - left, ToRasterY(area.Y + area.Height) - top);
    }

    /// <summary>
    /// A logical thickness in raster pixels, rounded on its own rather than from where it sits, so
    /// a 1 px edge is as thick on one side of an element as on the other. Never rounded away.
    /// </summary>
    internal int ToRasterThickness(int thickness) => thickness <= 0 ? 0 : Math.Max(1, Round(thickness));

    /// <summary>
    /// Makes the edges of <paramref name="logical"/> land on those of <paramref name="raster"/>
    /// until the returned scope is disposed. A border rounds its thickness on its own, so the inside
    /// it leaves is not where rounding the logical inside would put it; snapping the inside to it is
    /// what keeps a fill drawn there from leaving a gap against the border or covering it.
    /// </summary>
    internal SnapScope PushSnap(Rectangle logical, Rectangle raster)
    {
        _snaps.Add((logical, raster));
        return new SnapScope(this, _snaps.Count, _generation);
    }

    private int Round(int value) => Scale == 1f ? value : (int)Math.Floor((value * (double)Scale) + 0.5);

    private void Emit(Rectangle area, Texture? texture, Vector4 uvs, LinearColor tint, TextureFilter filter)
    {
        // A quad with nothing visible costs a draw call and can produce an empty or negative
        // scissor, so it is dropped here rather than defended against in the renderer.
        if (area.Width <= 0 || area.Height <= 0)
        {
            return;
        }

        Rectangle clip = CurrentRasterClip;
        Rectangle visible = clip.Intersect(area);
        if (visible.Width <= 0 || visible.Height <= 0)
        {
            return;
        }

        _instructions.Add(new PaintInstruction(area, clip, texture, uvs, tint, filter));
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
        _rasterClipStack.RemoveAt(_rasterClipStack.Count - 1);
    }

    internal void PopSnapTo(int depth, int generation)
    {
        if (generation != _generation || _snaps.Count != depth)
        {
            return;
        }

        _snaps.RemoveAt(_snaps.Count - 1);
    }

    /// <summary>
    /// Restores the clip and snap depths when the returned scope is disposed, so an unbalanced
    /// custom drawable stays a local bug instead of corrupting the rest of the frame.
    /// </summary>
    internal IsolationScope Isolate() => new(this, _clipStack.Count, _snaps.Count);

    private void TruncateTo(int clipDepth, int snapDepth)
    {
        while (_clipStack.Count > clipDepth)
        {
            _clipStack.RemoveAt(_clipStack.Count - 1);
            _rasterClipStack.RemoveAt(_rasterClipStack.Count - 1);
        }

        while (_snaps.Count > snapDepth)
        {
            _snaps.RemoveAt(_snaps.Count - 1);
        }
    }

    /// <summary>
    /// Restores the depths it was taken at. A ref struct rather than a callback, because the
    /// paint traversal enters one of these twice per element and a delegate would allocate a
    /// closure over the element each time.
    /// </summary>
    internal readonly ref struct IsolationScope
    {
        private readonly PaintContext _context;
        private readonly int _clipDepth;
        private readonly int _snapDepth;

        internal IsolationScope(PaintContext context, int clipDepth, int snapDepth)
        {
            _context = context;
            _clipDepth = clipDepth;
            _snapDepth = snapDepth;
        }

        public void Dispose() => _context?.TruncateTo(_clipDepth, _snapDepth);
    }

    /// <summary>Holds a pushed snap, released the way a <see cref="ClipScope"/> releases its clip.</summary>
    internal readonly ref struct SnapScope
    {
        private readonly PaintContext _context;
        private readonly int _depth;
        private readonly int _generation;

        internal SnapScope(PaintContext context, int depth, int generation)
        {
            _context = context;
            _depth = depth;
            _generation = generation;
        }

        public void Dispose() => _context?.PopSnapTo(_depth, _generation);
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
