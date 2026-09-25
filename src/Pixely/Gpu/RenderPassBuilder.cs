using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Pixely.Gpu;

/// <summary>
/// Collects the attachments of a render pass and begins it on a <see cref="CommandBuffer"/>. It lives on the stack, and its
/// methods return it by <c>ref</c>, so a chain such as <c>new RenderPassBuilder(commandBuffer).AddColorTarget(texture).Build()</c>
/// fills one instance without allocating or copying it. A method that fills a builder it is given must take it by <c>ref</c>:
/// passed by value, the method fills a copy. <see cref="Build"/> empties the builder, so a local can be built again.
/// </summary>
public ref struct RenderPassBuilder
{
    // SDL's limit on color targets per pass (MAX_COLOR_TARGET_BINDINGS).
    private const int MaxColorTargets = 8;

    private readonly CommandBuffer _commandBuffer;
    private ColorTargetBuffer _colorTargets;
    private int _colorTargetCount;
    private ColorTargetSettingsBuffer _colorTargetSettings;
    private int _colorTargetSettingsCount;
    private ColorTargetSettings? _sharedColorTargetSettings;
    private Texture? _depthBuffer;
    private DepthBufferSettings _depthBufferSettings = DepthBufferSettings.Default;

    public RenderPassBuilder(CommandBuffer commandBuffer)
    {
        _commandBuffer = commandBuffer;
    }

    [UnscopedRef]
    public ref RenderPassBuilder AddColorTarget(Texture texture)
    {
        ThrowIfColorTargetsFull();
        _colorTargets[_colorTargetCount++] = texture;
        return ref this;
    }

    [UnscopedRef]
    public ref RenderPassBuilder AddColorTargets(scoped ReadOnlySpan<Texture> textures)
    {
        foreach (Texture texture in textures)
        {
            AddColorTarget(texture);
        }
        return ref this;
    }

    [UnscopedRef]
    public ref RenderPassBuilder AddColorTarget(Texture texture, ColorTargetSettings settings)
    {
        ThrowIfColorTargetsFull();
        _colorTargets[_colorTargetCount++] = texture;
        _colorTargetSettings[_colorTargetSettingsCount++] = settings;
        return ref this;
    }

    [UnscopedRef]
    public ref RenderPassBuilder SetSharedColorTargetSettings(ColorTargetSettings settings)
    {
        _sharedColorTargetSettings = settings;
        return ref this;
    }

    [UnscopedRef]
    public ref RenderPassBuilder SetDepthBuffer(Texture depthBuffer, DepthBufferSettings settings)
    {
        _depthBuffer = depthBuffer;
        _depthBufferSettings = settings;
        return ref this;
    }

    public RenderPass Build()
    {
        if (_commandBuffer == null)
        {
            throw new InvalidOperationException($"A {nameof(RenderPassBuilder)} needs a command buffer. Create it with new {nameof(RenderPassBuilder)}(commandBuffer).");
        }

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

        if (hasShared)
        {
            for (int i = 0; i < _colorTargetCount; i++)
            {
                _colorTargetSettings[i] = _sharedColorTargetSettings!;
            }
        }

        ReadOnlySpan<Texture> colorTargets = ((ReadOnlySpan<Texture>)_colorTargets)[.._colorTargetCount];
        ReadOnlySpan<ColorTargetSettings> colorTargetSettings = ((ReadOnlySpan<ColorTargetSettings>)_colorTargetSettings)[.._colorTargetCount];
        RenderPass renderPass = _commandBuffer.CreateRenderPass(colorTargets, colorTargetSettings, _depthBuffer, _depthBufferSettings);

        // A builder kept in a local and built again starts empty.
        _colorTargets = default;
        _colorTargetCount = 0;
        _colorTargetSettings = default;
        _colorTargetSettingsCount = 0;
        _sharedColorTargetSettings = null;
        _depthBuffer = null;
        _depthBufferSettings = DepthBufferSettings.Default;

        return renderPass;
    }

    private readonly void ThrowIfColorTargetsFull()
    {
        if (_colorTargetCount == MaxColorTargets)
        {
            throw new InvalidOperationException($"A render pass takes at most {MaxColorTargets} color targets.");
        }
    }

    [InlineArray(MaxColorTargets)]
    private struct ColorTargetBuffer
    {
        private Texture _element;
    }

    [InlineArray(MaxColorTargets)]
    private struct ColorTargetSettingsBuffer
    {
        private ColorTargetSettings _element;
    }
}
