using System.Numerics;
using System.Runtime.InteropServices;
using Pixely.Content;
using Pixely.Gpu;
using Pixely.Models;
using Pixely.RenderOrchestration;

namespace Pixely.Tutorials.SkinnedModel;

public sealed class ModelRenderer : IRenderer<BasicRenderContext>
{
    private const DepthBufferFormat DepthFormat = DepthBufferFormat.Depth32;

    private static readonly ColorTargetSettings _clearSettings = new() { ClearColorValue = new FColor(0.18f, 0.2f, 0.24f, 1f) };
    private static readonly DepthBufferSettings _depthSettings = new() { DepthBufferStoreOperation = StoreOperation.DontCare };
    private static readonly Vector3 _cameraPosition = new(0, 0.9f, 4.2f);
    private static readonly Vector3 _cameraTarget = new(0, 0.75f, 0);
    private static readonly Vector3 _lightDirection = Vector3.Normalize(new Vector3(-0.4f, -0.6f, -1f));

    private readonly AnimatedModel _animatedModel;
    private readonly GpuDevice _gpuDevice;
    private readonly Pipelines _staticPipelines;
    private readonly Pipelines _skinnedPipelines;
    private readonly Texture _whiteTexture;
    private readonly Dictionary<SamplerConfig, Sampler> _samplers = new();
    private Texture? _depthBuffer;

    private ModelRenderer(AnimatedModel animatedModel, GpuDevice gpuDevice, Pipelines staticPipelines, Pipelines skinnedPipelines, Texture whiteTexture)
    {
        _animatedModel = animatedModel;
        _gpuDevice = gpuDevice;
        _staticPipelines = staticPipelines;
        _skinnedPipelines = skinnedPipelines;
        _whiteTexture = whiteTexture;
    }

    public void Render(BasicRenderContext renderContext)
    {
        SwapchainTexture swapchainTexture = renderContext.SwapchainTexture;
        if (_depthBuffer == null || _depthBuffer.Size != swapchainTexture.Size)
        {
            _depthBuffer?.Dispose();
            _depthBuffer = _gpuDevice.CreateDepthBufferTexture(swapchainTexture.Size, DepthFormat);
        }

        // glTF is right-handed with +Y up, so the camera is too. Pixely.Utils.PerspectiveCamera is left-handed and would mirror the model.
        float aspectRatio = (float)swapchainTexture.Size.Width / swapchainTexture.Size.Height;
        Matrix4x4 viewProjection = Matrix4x4.CreateLookAt(_cameraPosition, _cameraTarget, Vector3.UnitY)
                                   * Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 4, aspectRatio, 0.1f, 100f);
        // A swapchain without an sRGB format stores what the shader writes, so the shader encodes its linear result itself.
        bool encodeSrgb = swapchainTexture.Format is not (TextureFormat.B8G8R8A8UnormSrgb or TextureFormat.R8G8B8A8UnormSrgb);

        CommandBuffer commandBuffer = renderContext.CommandBuffer;
        using RenderPass renderPass = new RenderPassBuilder(commandBuffer)
            .AddColorTarget(swapchainTexture, _clearSettings)
            .SetDepthBuffer(_depthBuffer, _depthSettings)
            .Build();

        Model model = _animatedModel.Model;
        for (int copy = 0; copy < _animatedModel.Copies.Length; copy++)
        {
            ModelCopy modelCopy = _animatedModel.Copies[copy];
            foreach (int nodeIndex in _animatedModel.MeshNodes)
            {
                ModelNode node = model.Nodes[nodeIndex];
                Matrix4x4 world = _animatedModel.World[nodeIndex] * modelCopy.Placement;
                Matrix4x4.Invert(world, out Matrix4x4 inverseWorld);
                commandBuffer.PushVertexUniformData(0, new VertexConstants(viewProjection, world, Matrix4x4.Transpose(inverseWorld)));

                foreach (ModelPrimitive primitive in model.Meshes[node.Mesh!.Value].Primitives)
                {
                    ModelMaterial material = primitive.Material is { } materialIndex ? model.Materials[materialIndex] : ModelMaterial.Default;
                    bool skinned = node.Skin is not null;
                    Winding winding = skinned
                        ? _animatedModel.GetWinding(copy, node.Skin!.Value)
                        : world.GetDeterminant() < 0 ? Winding.Clockwise : Winding.CounterClockwise;
                    bool doubleSided = material.DoubleSided || modelCopy.ForceDoubleSided;

                    renderPass.BindGraphicsPipeline((skinned ? _skinnedPipelines : _staticPipelines).Select(doubleSided, winding));
                    renderPass.BindVertexBuffer(0, primitive.Vertices);
                    if (skinned)
                    {
                        renderPass.BindVertexBuffer(1, primitive.Skin!);
                        renderPass.BindVertexStorageBuffer(_animatedModel.GetJointBuffer(copy, node.Skin!.Value));
                    }

                    renderPass.BindIndexBuffer(primitive.Indices);
                    // The glTF default for a missing texture is white, so the factor alone gives the color.
                    ModelTexture? texture = material.BaseColorTexture;
                    renderPass.BindFragmentSampler(texture?.Texture ?? _whiteTexture, GetSampler(texture?.Sampler ?? SamplerConfig.Linear));

                    float alphaCutoff = material.AlphaMode == AlphaMode.Mask ? material.AlphaCutoff : -1f;
                    commandBuffer.PushFragmentUniformData(0, new FragmentConstants(
                        material.BaseColorFactor, new Vector4(_lightDirection, 0), new Vector4(_cameraPosition, 1), alphaCutoff, encodeSrgb ? 1u : 0u));

                    renderPass.DrawIndexedPrimitive();
                }
            }
        }
    }

    private Sampler GetSampler(SamplerConfig config)
    {
        if (!_samplers.TryGetValue(config, out Sampler? sampler))
        {
            sampler = _gpuDevice.CreateSampler(config);
            _samplers.Add(config, sampler);
        }

        return sampler;
    }

    public static ModelRenderer Create(AnimatedModel animatedModel, GpuDevice gpuDevice, GraphicsPipelineBuilder graphicsPipelineBuilder, GpuMemorySystem gpuMemorySystem)
    {
        Pipelines staticPipelines = Pipelines.Build(graphicsPipelineBuilder, "shaders/model_static", skinned: false);
        Pipelines skinnedPipelines = Pipelines.Build(graphicsPipelineBuilder, "shaders/model_skinned", skinned: true);
        Texture whiteTexture = gpuMemorySystem.CreateTexture(new RawImage([255, 255, 255, 255], new ShortSize(1, 1), PixelFormat.Abgr8888), TextureFormat.R8G8B8A8UnormSrgb);
        return new ModelRenderer(animatedModel, gpuDevice, staticPipelines, skinnedPipelines, whiteTexture);
    }

    // One program in the three states a draw can need: culling back faces that wind either way, or no culling.
    private sealed record Pipelines(GraphicsPipeline CounterClockwise, GraphicsPipeline Clockwise, GraphicsPipeline NoCulling)
    {
        public GraphicsPipeline Select(bool doubleSided, Winding winding) => doubleSided ? NoCulling : winding switch
        {
            Winding.CounterClockwise => CounterClockwise,
            Winding.Clockwise => Clockwise,
            _ => NoCulling
        };

        public static Pipelines Build(GraphicsPipelineBuilder builder, string program, bool skinned) => new(
            Build(builder, program, skinned, CullMode.Back, FrontFace.CounterClockwise),
            Build(builder, program, skinned, CullMode.Back, FrontFace.Clockwise),
            Build(builder, program, skinned, CullMode.None, FrontFace.CounterClockwise));

        private static GraphicsPipeline Build(GraphicsPipelineBuilder builder, string program, bool skinned, CullMode cullMode, FrontFace frontFace)
        {
            builder.AddVertexBufferConfig<PositionTextureNormalVertex>();
            if (skinned)
            {
                builder.AddVertexBufferConfig<SkinVertex>();
            }

            return builder
                .SetShaderProgram(program)
                .AddColorFormatFromDisplay()
                .EnableDepthTesting(DepthFormat)
                .SetCullMode(cullMode)
                .SetFrontFace(frontFace)
                .Build();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct VertexConstants(Matrix4x4 ViewProjection, Matrix4x4 World, Matrix4x4 NormalMatrix);

    // Matches FragmentConstants in model_common.slang, padded to 64 bytes.
    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct FragmentConstants(Vector4 BaseColorFactor, Vector4 LightDirection, Vector4 CameraPosition, float AlphaCutoff, uint EncodeSrgb)
    {
        private readonly ulong _padding;
    }
}
