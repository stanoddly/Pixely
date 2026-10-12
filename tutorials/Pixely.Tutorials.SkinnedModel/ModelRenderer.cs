using System.Collections.Immutable;
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

    // Left: mirrored on X, so it draws with clockwise front faces. Middle: as loaded. Right: drawn as if double-sided, with no culling.
    private static readonly ModelCopy[] _copies =
    [
        new(Matrix4x4.CreateScale(-1, 1, 1) * Matrix4x4.CreateTranslation(-1.4f, 0, 0), ForceDoubleSided: false),
        new(Matrix4x4.Identity, ForceDoubleSided: false),
        new(Matrix4x4.CreateTranslation(1.4f, 0, 0), ForceDoubleSided: true)
    ];

    private readonly AnimatedModel _animatedModel;
    private readonly ImmutableArray<int> _meshNodes;
    private readonly GpuDevice _gpuDevice;
    private readonly Pipelines _staticPipelines;
    private readonly Pipelines _skinnedPipelines;
    private readonly Texture _whiteTexture;
    private readonly Dictionary<SamplerConfig, Sampler> _samplers = new();
    private Texture? _depthBuffer;

    private ModelRenderer(AnimatedModel animatedModel, ImmutableArray<int> meshNodes, GpuDevice gpuDevice, Pipelines staticPipelines, Pipelines skinnedPipelines, Texture whiteTexture)
    {
        _animatedModel = animatedModel;
        _meshNodes = meshNodes;
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

        ModelInstance instance = _animatedModel.Instance;
        Model model = instance.Model;
        foreach (ModelCopy copy in _copies)
        {
            foreach (int nodeIndex in _meshNodes)
            {
                ModelNode node = model.Nodes[nodeIndex];
                int? skin = node.Skin;
                // glTF ignores the transform of a skinned mesh's own node, because its joint matrices already place it in the scene.
                // The skinned shader applies the placement after the joints.
                Matrix4x4 world = skin is null ? instance.WorldMatrices[nodeIndex] * copy.Placement : copy.Placement;
                Winding winding = skin is null ? ModelInstance.GetWinding(world) : instance.GetSkinWinding(skin.Value, copy.Placement);
                commandBuffer.PushVertexUniformData(0, new VertexConstants(viewProjection, world, CreateNormalMatrix(world)));

                foreach (ModelPrimitive primitive in model.Meshes[node.Mesh!.Value].Primitives)
                {
                    ModelMaterial material = primitive.Material is { } materialIndex ? model.Materials[materialIndex] : ModelMaterial.Default;
                    bool doubleSided = material.DoubleSided || copy.ForceDoubleSided;

                    renderPass.BindGraphicsPipeline((skin is null ? _staticPipelines : _skinnedPipelines).Select(doubleSided, winding));
                    renderPass.BindVertexBuffer(0, primitive.Vertices);
                    if (skin is not null)
                    {
                        renderPass.BindVertexBuffer(1, primitive.Skin!);
                        renderPass.BindVertexStorageBuffer(instance.GetJointBuffer(skin.Value));
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

    // Normals transform by the inverse transpose of the upper 3x3, which is its cofactor matrix divided by its determinant. The
    // cofactor matrix exists even when the inverse does not, such as for a node scaled to zero on one axis, and multiplying by the
    // determinant's sign instead of dividing keeps mirrored normals pointing outward. The shader normalizes the result, the same
    // as model_skinned.slang does for joints.
    private static Matrix4x4 CreateNormalMatrix(Matrix4x4 world)
    {
        Vector3 r0 = new(world.M11, world.M12, world.M13);
        Vector3 r1 = new(world.M21, world.M22, world.M23);
        Vector3 r2 = new(world.M31, world.M32, world.M33);
        float sign = Vector3.Dot(r0, Vector3.Cross(r1, r2)) < 0 ? -1 : 1;
        Vector3 c0 = sign * Vector3.Cross(r1, r2);
        Vector3 c1 = sign * Vector3.Cross(r2, r0);
        Vector3 c2 = sign * Vector3.Cross(r0, r1);
        return new Matrix4x4(c0.X, c0.Y, c0.Z, 0, c1.X, c1.Y, c1.Z, 0, c2.X, c2.Y, c2.Z, 0, 0, 0, 0, 1);
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
        // The default scene, or the first one when the file names none, as glTF viewers do.
        Model model = animatedModel.Instance.Model;
        ImmutableArray<int> meshNodes = model.Scenes.IsEmpty ? [] : model.Scenes[model.DefaultScene ?? 0].MeshNodes;
        return new ModelRenderer(animatedModel, meshNodes, gpuDevice, staticPipelines, skinnedPipelines, whiteTexture);
    }

    // One copy of the model on screen: where it stands, and whether it is drawn as if its materials were double-sided. The copies
    // share one pose, since they show the same model at the same time.
    private sealed record ModelCopy(Matrix4x4 Placement, bool ForceDoubleSided);

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
