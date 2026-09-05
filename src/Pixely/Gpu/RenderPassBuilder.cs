using System.Runtime.CompilerServices;

namespace Pixely.Gpu;

/// <summary>
/// Describes a render pass and creates it. A value type with inline storage, so building a pass
/// every frame costs nothing on the heap. Copies are independent: passing a builder around or
/// building from the same value twice does not share state.
/// </summary>
public struct RenderPassBuilder
{
    // SDL_GPU accepts at most four color targets in a single render pass.
    public const int MaxColorTargets = 4;

    [InlineArray(MaxColorTargets)]
    private struct ColorTargetArray
    {
        private Texture _element0;
    }

    [InlineArray(MaxColorTargets)]
    private struct ColorTargetSettingsArray
    {
        private ColorTargetSettings _element0;
    }

    private readonly CommandBuffer _commandBuffer;
    private ColorTargetArray _colorTargets;
    private ColorTargetSettingsArray _colorTargetSettings;
    private int _colorTargetCount;
    private int _colorTargetSettingsCount;
    private Texture? _depthBuffer;
    private DepthBufferSettings _depthBufferSettings;
    private ColorTargetSettings? _sharedColorTargetSettings;

    public RenderPassBuilder(CommandBuffer commandBuffer)
    {
        _commandBuffer = commandBuffer;
        _depthBufferSettings = DepthBufferSettings.Default;
    }

    public RenderPassBuilder AddColorTarget(Texture texture)
    {
        ThrowIfColorTargetsFull();
        _colorTargets[_colorTargetCount] = texture;
        _colorTargetCount++;
        return this;
    }

    public RenderPassBuilder AddColorTarget(Texture texture, ColorTargetSettings settings)
    {
        ThrowIfColorTargetsFull();
        _colorTargets[_colorTargetCount] = texture;
        _colorTargetCount++;
        _colorTargetSettings[_colorTargetSettingsCount] = settings;
        _colorTargetSettingsCount++;
        return this;
    }

    public RenderPassBuilder AddColorTargets(ReadOnlySpan<Texture> textures)
    {
        foreach (Texture texture in textures)
        {
            ThrowIfColorTargetsFull();
            _colorTargets[_colorTargetCount] = texture;
            _colorTargetCount++;
        }
        return this;
    }

    public RenderPassBuilder SetSharedColorTargetSettings(ColorTargetSettings settings)
    {
        _sharedColorTargetSettings = settings;
        return this;
    }

    public RenderPassBuilder SetDepthBuffer(Texture depthBuffer, DepthBufferSettings settings)
    {
        _depthBuffer = depthBuffer;
        _depthBufferSettings = settings;
        return this;
    }

    public readonly IRenderPass Build()
    {
        bool hasShared = _sharedColorTargetSettings != null;
        bool hasPerTarget = _colorTargetSettingsCount > 0;
        bool hasColorTargets = _colorTargetCount > 0;
        bool hasDepthBuffer = _depthBuffer != null;

        if (hasShared && hasPerTarget)
        {
            throw new InvalidOperationException("Cannot have both shared and per-target settings set at once.");
        }

        if (hasColorTargets && !hasShared && !hasPerTarget)
        {
            throw new InvalidOperationException("Must have either shared or per-target settings set when using color targets.");
        }

        if (hasPerTarget && _colorTargetSettingsCount != _colorTargetCount)
        {
            throw new InvalidOperationException("Every color target needs its own settings when per-target settings are used.");
        }

        if (!hasColorTargets && !hasDepthBuffer)
        {
            throw new InvalidOperationException("At least one color target or a depth buffer is required.");
        }

        ColorTargetArray colorTargets = _colorTargets;
        ColorTargetSettingsArray colorTargetSettings = _colorTargetSettings;
        Span<Texture> colorTargetSpan = colorTargets;
        Span<ColorTargetSettings> colorTargetSettingsSpan = colorTargetSettings;

        if (hasShared)
        {
            colorTargetSettingsSpan[.._colorTargetCount].Fill(_sharedColorTargetSettings!);
        }

        return _commandBuffer.CreateRenderPass(
            colorTargetSpan[.._colorTargetCount],
            colorTargetSettingsSpan[.._colorTargetCount],
            _depthBuffer,
            _depthBufferSettings);
    }

    private readonly void ThrowIfColorTargetsFull()
    {
        if (_colorTargetCount == MaxColorTargets)
        {
            throw new InvalidOperationException($"A render pass cannot have more than {MaxColorTargets} color targets.");
        }
    }
}
