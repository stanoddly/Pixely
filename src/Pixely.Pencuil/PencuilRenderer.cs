using System.Numerics;
using Pixely.Gpu;
using Pixely.RenderOrchestration;
using Pixely.Shaders;

namespace Pixely.Pencuil;

internal sealed class PencuilRenderer<TRenderContext> : IRenderer<TRenderContext>
    where TRenderContext : IRenderContext
{
    private static readonly ColorTargetSettings _guiColorTargetSettings = new()
    {
        ClearColorValue = FColors.Transparent
    };

    private static readonly Matrix4x4 _presentViewProjection =
        Matrix4x4.CreateOrthographicOffCenterLeftHanded(0, 1, 1, 0, 0, 1);

    private static readonly Vector4 _fullTextureUvs = new(0, 0, 1, 1);

    private readonly GpuVertexBuffer<PositionTextureVertex> _vertexBuffer;
    private readonly GraphicsPipeline _colorPipeline;
    private readonly GraphicsPipeline _tintedTexturePipeline;
    private readonly GraphicsPipeline _presentPipeline;
    private readonly Sampler _sampler;
    private readonly GpuDevice _gpuDevice;
    private readonly TextureFormat _colorTargetFormat;
    private readonly Pencil _pencil;
    private readonly bool _clearTarget;

    private Texture _retainedTexture;
    private Texture _depthBuffer;
    private Matrix4x4 _viewProjection;
    private int _maxDepthValue;
    private bool _retainedTextureDirty;

    public int Order { get; }
    public ViewScope ViewScope { get; }

    internal PencuilRenderer(
        Pencuil pencuil,
        int order,
        bool clearTarget,
        GraphicsPipelineBuilder graphicsPipelineBuilder,
        GpuMemorySystem gpuMemorySystem,
        ShaderLoader shaderLoader,
        GpuDevice gpuDevice,
        Window window)
    {
        Pencil pencil = pencuil.Pencil;
        ViewScope = pencuil.ViewScope;
        ReadOnlySpan<PositionTextureVertex> quad =
        [
            new(new Vector3(0.0f, 0.0f, 0.0f), new Vector2(0, 0)),
            new(new Vector3(1.0f, 0.0f, 0.0f), new Vector2(1, 0)),
            new(new Vector3(0.0f, 1.0f, 0.0f), new Vector2(0, 1)),
            new(new Vector3(1.0f, 1.0f, 0.0f), new Vector2(1, 1)),
        ];

        _vertexBuffer = gpuMemorySystem.CreateVertexBuffer(quad);

        GraphicsShaderProgram colorShaderProgram = shaderLoader.LoadGraphicsShaderProgram("shaders/pencuil_color");
        GraphicsShaderProgram tintedTextureShaderProgram = shaderLoader.LoadGraphicsShaderProgram("shaders/pencuil_tinted_texture");
        GraphicsShaderProgram textureShaderProgram = shaderLoader.LoadGraphicsShaderProgram("shaders/pencuil_texture");

        TextureFormat colorTargetFormat = window.ColorTargetFormat;
        ShortSize renderSize = window.RenderSizeInPixels;

        _colorPipeline = graphicsPipelineBuilder
            .SetPrimitiveType(PrimitiveType.TriangleStrip)
            .AddVertexBufferConfigBasedOnBuffer(_vertexBuffer)
            .SetShaderProgram(colorShaderProgram)
            .AddColorTarget(colorTargetFormat, BlendingState.Standard)
            .EnableDepthTesting(DepthBufferFormat.Depth32)
            .SetCullMode(CullMode.None)
            .Build();

        _tintedTexturePipeline = graphicsPipelineBuilder
            .SetPrimitiveType(PrimitiveType.TriangleStrip)
            .AddVertexBufferConfigBasedOnBuffer(_vertexBuffer)
            .SetShaderProgram(tintedTextureShaderProgram)
            .AddColorTarget(colorTargetFormat, BlendingState.PremultipliedAlpha)
            .EnableDepthTesting(DepthBufferFormat.Depth32)
            .SetCullMode(CullMode.None)
            .Build();

        _presentPipeline = graphicsPipelineBuilder
            .SetPrimitiveType(PrimitiveType.TriangleStrip)
            .AddVertexBufferConfigBasedOnBuffer(_vertexBuffer)
            .SetShaderProgram(textureShaderProgram)
            .AddColorTarget(colorTargetFormat, BlendingState.Standard)
            .Build();

        _gpuDevice = gpuDevice;
        _colorTargetFormat = colorTargetFormat;
        _pencil = pencil;
        _clearTarget = clearTarget;
        Order = order;

        _sampler = gpuDevice.CreateSampler(SamplerConfig.PixelArt);
        _retainedTexture = gpuDevice.CreateColorTargetTexture(renderSize, colorTargetFormat);
        _depthBuffer = gpuDevice.CreateDepthBufferTexture(renderSize, DepthBufferFormat.Depth32);

        _viewProjection = Matrix4x4.CreateOrthographicOffCenterLeftHanded(0, renderSize.Width, renderSize.Height, 0, 0, 1);
    }

    public void Render(TRenderContext renderContext)
    {
        ShortSize targetSize = renderContext.ColorTarget.Size;
        ResizeRetainedTextureIfNeeded(targetSize);

        if (_pencil.ViewportSize != targetSize)
        {
            _pencil.UpdateViewport(targetSize.Width, targetSize.Height);
        }

        if (_pencil.CompletedInstructionViewportSize != _pencil.ViewportSize)
        {
            Clear(renderContext.CommandBuffer);
            _retainedTextureDirty = true;
            Present(renderContext.CommandBuffer, renderContext.ColorTarget);
            return;
        }

        // Retained texture dirtiness forces a redraw even when instruction content is
        // unchanged, since the retained texture itself was just resized.
        if (_pencil.RenderDirty || _retainedTextureDirty)
        {
            RenderPencil(renderContext.CommandBuffer);
            _pencil.RenderDirty = false;
            _retainedTextureDirty = false;
        }

        Present(renderContext.CommandBuffer, renderContext.ColorTarget);
    }

    private void ResizeRetainedTextureIfNeeded(ShortSize newSize)
    {
        if (_retainedTexture.Size == newSize)
        {
            return;
        }

        _retainedTexture.Dispose();
        _depthBuffer.Dispose();

        _retainedTexture = _gpuDevice.CreateColorTargetTexture(newSize, _colorTargetFormat);
        _depthBuffer = _gpuDevice.CreateDepthBufferTexture(newSize, DepthBufferFormat.Depth32);
        _viewProjection = Matrix4x4.CreateOrthographicOffCenterLeftHanded(0, newSize.Width, newSize.Height, 0, 0, 1);
        _retainedTextureDirty = true;
    }

    private void RenderPencil(CommandBuffer commandBuffer)
    {
        List<ColoredRectangleInstruction> coloredRectangleInstructions = _pencil.CompletedColoredRectangleInstructions;
        List<TextureRegionInstruction> textureRegionInstructions = _pencil.CompletedTextureRegionInstructions;

        if (coloredRectangleInstructions.Count == 0 && textureRegionInstructions.Count == 0)
        {
            Clear(commandBuffer);
            return;
        }

        _maxDepthValue = coloredRectangleInstructions.Count + textureRegionInstructions.Count;

        using IRenderPass renderPass = new RenderPassBuilder(commandBuffer)
            .AddColorTarget(_retainedTexture, _guiColorTargetSettings)
            .SetDepthBuffer(_depthBuffer, DepthBufferSettings.Default)
            .Build();

        commandBuffer.PushVertexUniformData(0, _viewProjection);

        renderPass.BindGraphicsPipeline(_colorPipeline);
        renderPass.BindVertexBuffer(_vertexBuffer);

        foreach (ColoredRectangleInstruction instruction in coloredRectangleInstructions)
        {
            Matrix4x4 scaleMatrix = Matrix4x4.CreateScale(
                instruction.Area.Width,
                instruction.Area.Height,
                1.0f);

            Matrix4x4 translationMatrix = Matrix4x4.CreateTranslation(
                instruction.Area.X,
                instruction.Area.Y,
                CalculateZCoordinate(instruction.Depth));

            Matrix4x4 worldMatrix = scaleMatrix * translationMatrix;

            commandBuffer.PushVertexUniformData(1, worldMatrix);
            commandBuffer.PushFragmentUniformData(0, (FColor)instruction.Color);

            renderPass.DrawPrimitive();
        }

        if (textureRegionInstructions.Count > 0)
        {
            renderPass.BindGraphicsPipeline(_tintedTexturePipeline);
            renderPass.BindVertexBuffer(_vertexBuffer);

            foreach (TextureRegionInstruction instruction in textureRegionInstructions)
            {
                Matrix4x4 scaleMatrix = Matrix4x4.CreateScale(
                    instruction.Area.Width,
                    instruction.Area.Height,
                    1.0f);

                Matrix4x4 translationMatrix = Matrix4x4.CreateTranslation(
                    instruction.Area.X,
                    instruction.Area.Y,
                    CalculateZCoordinate(instruction.Depth));

                Matrix4x4 worldMatrix = scaleMatrix * translationMatrix;

                commandBuffer.PushVertexUniformData(1, worldMatrix);
                commandBuffer.PushFragmentUniformData(0, instruction.Uvs);
                commandBuffer.PushFragmentUniformData(1, instruction.Tint);

                renderPass.BindFragmentSampler(instruction.Texture, _sampler);
                renderPass.DrawPrimitive();
            }
        }
    }

    private void Clear(CommandBuffer commandBuffer)
    {
        using IRenderPass clearPass = new RenderPassBuilder(commandBuffer)
            .AddColorTarget(_retainedTexture, _guiColorTargetSettings)
            .Build();
    }

    private void Present(CommandBuffer commandBuffer, Texture target)
    {
        ColorTargetSettings settings = _clearTarget
            ? ColorTargetSettings.Clear
            : new ColorTargetSettings { LoadOperation = LoadOperation.Load };

        using IRenderPass presentPass = new RenderPassBuilder(commandBuffer)
            .AddColorTarget(target, settings)
            .Build();

        commandBuffer.PushVertexUniformData(0, _presentViewProjection);
        commandBuffer.PushVertexUniformData(1, Matrix4x4.Identity);
        commandBuffer.PushFragmentUniformData(0, _fullTextureUvs);

        presentPass.BindGraphicsPipeline(_presentPipeline);
        presentPass.BindVertexBuffer(_vertexBuffer);
        presentPass.BindFragmentSampler(_retainedTexture, _sampler);
        presentPass.DrawPrimitive();
    }

    private float CalculateZCoordinate(int elementDepth)
    {
        return (_maxDepthValue - elementDepth) / (float)(_maxDepthValue + 1);
    }
}
