using System.Numerics;
using Pixely.Gpu;
using Pixely.RenderOrchestration;
using Pixely.Text;

namespace Pixely.Tutorials.ImageTextBrowser;

/// <summary>
/// Draws a decoded PNG on the left of the canvas and a rasterised line of text on the right, so
/// that SDL3_image and SDL3_ttf are both exercised through Pixely's own interfaces rather than
/// merely linked. The shader is the Image Loading tutorial's, unchanged: one sampled texture, one
/// sampler, no transform, so the quad's clip-space coordinates alone place each half.
/// </summary>
public class ImageTextRenderer : IRenderer<BasicRenderContext>
{
    private readonly GraphicsPipeline _graphicsPipeline;
    private readonly GpuVertexBuffer<PositionTextureVertex> _imageQuad;
    private readonly GpuVertexBuffer<PositionTextureVertex> _textQuad;
    private readonly Texture _imageTexture;
    private readonly Texture _textTexture;
    private readonly Sampler _sampler;

    public ImageTextRenderer(
        GraphicsPipeline graphicsPipeline,
        GpuVertexBuffer<PositionTextureVertex> imageQuad,
        GpuVertexBuffer<PositionTextureVertex> textQuad,
        Texture imageTexture,
        Texture textTexture,
        Sampler sampler)
    {
        _graphicsPipeline = graphicsPipeline;
        _imageQuad = imageQuad;
        _textQuad = textQuad;
        _imageTexture = imageTexture;
        _textTexture = textTexture;
        _sampler = sampler;
    }

    public void Render(BasicRenderContext renderContext)
    {
        using IRenderPass renderPass = new RenderPassBuilder(renderContext.CommandBuffer)
            .AddColorTarget(renderContext.SwapchainTexture)
            .SetSharedColorTargetSettings(ColorTargetSettings.Clear)
            .Build();

        renderPass.BindGraphicsPipeline(_graphicsPipeline);

        renderPass.BindVertexBuffer(_imageQuad);
        renderPass.BindFragmentSampler(_imageTexture, _sampler);
        renderPass.DrawPrimitive();

        renderPass.BindVertexBuffer(_textQuad);
        renderPass.BindFragmentSampler(_textTexture, _sampler);
        renderPass.DrawPrimitive();
    }

    /// <summary>
    /// A triangle-strip quad covering the given clip-space rectangle, with the texture mapped over
    /// it. Clip space runs left to right and bottom to top, so the vertical texture coordinates are
    /// the inverse of the positions. The vertex order matches
    /// <see cref="PositionTextureShapes.VerticalQuad"/>: reversing it winds the triangles the other
    /// way and the pipeline culls them.
    /// </summary>
    private static PositionTextureVertex[] Quad(float left, float right, float bottom, float top)
    {
        return
        [
            new PositionTextureVertex(new Vector3(left, bottom, 0f), new Vector2(0f, 1f)),
            new PositionTextureVertex(new Vector3(left, top, 0f), new Vector2(0f, 0f)),
            new PositionTextureVertex(new Vector3(right, bottom, 0f), new Vector2(1f, 1f)),
            new PositionTextureVertex(new Vector3(right, top, 0f), new Vector2(1f, 0f))
        ];
    }

    public static ImageTextRenderer Create(
        GraphicsPipelineBuilder graphicsPipelineBuilder,
        GpuMemorySystem gpuMemorySystem,
        GpuDevice gpuDevice,
        ITextureLoader textureLoader,
        IFontSystem fontSystem)
    {
        Texture imageTexture = textureLoader.Load(ImageTextProbe.ImagePath);

        Font font = fontSystem.Load(ImageTextProbe.FontPath, ImageTextProbe.FontSize);
        TextSpriteAsset textSprite = fontSystem.CreateTextSprite(ImageTextProbe.Text, font);

        Sampler sampler = gpuDevice.CreateSampler(SamplerConfig.Linear);

        GraphicsPipeline graphicsPipeline = graphicsPipelineBuilder
            .SetPrimitiveType(PrimitiveType.TriangleStrip)
            .AddVertexBufferConfig<PositionTextureVertex>()
            .SetShaderProgram("shaders/shader")
            .AddColorFormatFromDisplay(BlendingState.PremultipliedAlpha)
            .Build();

        return new ImageTextRenderer(
            graphicsPipeline,
            gpuMemorySystem.CreateVertexBuffer<PositionTextureVertex>(Quad(-0.95f, -0.05f, -0.8f, 0.8f)),
            gpuMemorySystem.CreateVertexBuffer<PositionTextureVertex>(Quad(0.05f, 0.95f, -0.25f, 0.25f)),
            imageTexture,
            textSprite.Texture,
            sampler);
    }
}
