using System.Runtime.CompilerServices;

namespace Pixely.Gpu;

/// <summary>
/// Describes a render pass and creates it. A value type with inline storage, so building a pass
/// every frame costs nothing on the heap. Copies are independent: passing a builder around or
/// building from the same value twice does not share state.
/// </summary>
public struct RenderPassBuilder
{
    [InlineArray(CommandBuffer.MaxColorTargets)]
    private struct ColorTargetArray
    {
        private Texture _element0;
    }

    [InlineArray(CommandBuffer.MaxColorTargets)]
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

    // Every method configures a copy and returns it, so the receiver keeps whatever it already
    // described. A method that mutated this directly would also mutate the variable it was called on.
    public readonly RenderPassBuilder AddColorTarget(Texture texture)
    {
        ThrowIfCapacityExceeded(1);

        RenderPassBuilder builder = this;
        builder._colorTargets[builder._colorTargetCount] = texture;
        builder._colorTargetCount++;
        return builder;
    }

    public readonly RenderPassBuilder AddColorTarget(Texture texture, ColorTargetSettings settings)
    {
        ThrowIfCapacityExceeded(1);

        RenderPassBuilder builder = this;
        builder._colorTargets[builder._colorTargetCount] = texture;
        builder._colorTargetCount++;
        builder._colorTargetSettings[builder._colorTargetSettingsCount] = settings;
        builder._colorTargetSettingsCount++;
        return builder;
    }

    public readonly RenderPassBuilder AddColorTargets(ReadOnlySpan<Texture> textures)
    {
        ThrowIfCapacityExceeded(textures.Length);

        RenderPassBuilder builder = this;
        foreach (Texture texture in textures)
        {
            builder._colorTargets[builder._colorTargetCount] = texture;
            builder._colorTargetCount++;
        }
        return builder;
    }

    public readonly RenderPassBuilder SetSharedColorTargetSettings(ColorTargetSettings settings)
    {
        RenderPassBuilder builder = this;
        builder._sharedColorTargetSettings = settings;
        return builder;
    }

    public readonly RenderPassBuilder SetDepthBuffer(Texture depthBuffer, DepthBufferSettings settings)
    {
        RenderPassBuilder builder = this;
        builder._depthBuffer = depthBuffer;
        builder._depthBufferSettings = settings;
        return builder;
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

    private readonly void ThrowIfCapacityExceeded(int addedColorTargets)
    {
        if (_colorTargetCount + addedColorTargets > CommandBuffer.MaxColorTargets)
        {
            throw new InvalidOperationException($"A render pass cannot have more than {CommandBuffer.MaxColorTargets} color targets.");
        }
    }
}
