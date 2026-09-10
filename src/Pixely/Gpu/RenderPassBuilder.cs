using System.Runtime.InteropServices;

namespace Pixely.Gpu;

/// <summary>
/// Collects the description of a render pass across several statements, for callers that compose one
/// conditionally or from a varying number of targets. It allocates, so a renderer that describes the same
/// pass every frame should call <see cref="CommandBuffer.CreateRenderPass(Texture, ColorTargetSettings)"/>
/// or one of its overloads instead.
/// </summary>
public class RenderPassBuilder
{
    private readonly CommandBuffer _commandBuffer;
    private readonly List<Texture> _colorTargets = new();
    private readonly List<ColorTargetSettings> _colorTargetSettings = new();
    private Texture? _depthBuffer;
    private DepthBufferSettings _depthBufferSettings = DepthBufferSettings.Default;
    private ColorTargetSettings? _sharedColorTargetSettings;

    public RenderPassBuilder(CommandBuffer commandBuffer)
    {
        _commandBuffer = commandBuffer;
    }

    public RenderPassBuilder AddColorTarget(Texture texture)
    {
        _colorTargets.Add(texture);
        return this;
    }

    public RenderPassBuilder AddColorTarget(Texture texture, ColorTargetSettings settings)
    {
        _colorTargets.Add(texture);
        _colorTargetSettings.Add(settings);
        return this;
    }

    public RenderPassBuilder AddColorTargets(ReadOnlySpan<Texture> textures)
    {
        foreach (Texture texture in textures)
        {
            _colorTargets.Add(texture);
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

    public IRenderPass Build()
    {
        bool hasShared = _sharedColorTargetSettings != null;
        bool hasPerTarget = _colorTargetSettings.Count > 0;
        bool hasColorTargets = _colorTargets.Count > 0;
        bool hasDepthBuffer = _depthBuffer != null;

        if (hasShared && hasPerTarget)
        {
            throw new InvalidOperationException("Cannot have both shared and per-target settings set at once.");
        }

        if (hasColorTargets && !hasShared && !hasPerTarget)
        {
            throw new InvalidOperationException("Must have either shared or per-target settings set when using color targets.");
        }

        if (hasPerTarget && _colorTargetSettings.Count != _colorTargets.Count)
        {
            throw new InvalidOperationException("Every color target needs its own settings when per-target settings are used.");
        }

        if (!hasColorTargets && !hasDepthBuffer)
        {
            throw new InvalidOperationException("At least one color target or a depth buffer is required.");
        }

        if (hasShared)
        {
            for (int i = 0; i < _colorTargets.Count; i++)
            {
                _colorTargetSettings.Add(_sharedColorTargetSettings!);
            }
        }

        IRenderPass renderPass = _commandBuffer.CreateRenderPass(
            CollectionsMarshal.AsSpan(_colorTargets),
            CollectionsMarshal.AsSpan(_colorTargetSettings),
            _depthBuffer,
            _depthBufferSettings);

        ResetState();

        return renderPass;
    }

    private void ResetState()
    {
        _colorTargets.Clear();
        _colorTargetSettings.Clear();
        _depthBuffer = null;
        _depthBufferSettings = DepthBufferSettings.Default;
        _sharedColorTargetSettings = null;
    }
}
