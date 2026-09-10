# Subrenderers

Subrenderers are renderers that receive an existing RenderPass instead of creating their own. They're used for internal composition within an `IRenderer<T>`.

```
RenderCoordinator<T>
└─ IRenderer<T>[] (geometry, lighting, post-process renderers)
    └─ Subrenderers (multiple renderers sharing the same RenderPass)
```

The parent renderer creates the RenderPass and multiple subrenderers contribute to the same render targets.

## Basic Pattern

A subrenderer receives both a `CommandBuffer` and an `IRenderPass`:

```csharp
public class MeshSubrenderer
{
    private readonly GraphicsPipeline _graphicsPipeline;
    private readonly MeshBufferService _bufferService;
    private readonly Camera _camera;

    public MeshSubrenderer(GraphicsPipeline graphicsPipeline, MeshBufferService bufferService, Camera camera)
    {
        _graphicsPipeline = graphicsPipeline;
        _bufferService = bufferService;
        _camera = camera;
    }

    public void Render(CommandBuffer commandBuffer, IRenderPass renderPass)
    {
        // Don't create a new RenderPass - use the one provided

        Matrix4x4 viewProjection = _camera.ViewMatrix * _camera.ProjectionMatrix;
        commandBuffer.PushVertexUniformData(0, viewProjection);

        renderPass.BindGraphicsPipeline(_graphicsPipeline);

        foreach (var entry in _bufferService.RenderableEntries)
        {
            renderPass.BindVertexBuffer(entry.Buffer);
            commandBuffer.PushVertexUniformData(1, entry.WorldMatrix);
            renderPass.DrawPrimitive();
        }
    }

    public static MeshSubrenderer Create(/* dependencies */)
    {
        // Build pipeline, return instance
    }
}
```

## Composing Multiple Subrenderers

### Defining a Subrenderer Interface

Create an interface for the subrenderers composed by a specific renderer:

```csharp
public interface IGeometrySubrenderer : IOrderable
{
    void Render(CommandBuffer commandBuffer, IRenderPass renderPass);
}
```

Inherit from `IOrderable` (from Pixely) to control execution order.

### Implementing the Interface

```csharp
public class MeshSubrenderer : IGeometrySubrenderer
{
    private readonly GraphicsPipeline _graphicsPipeline;
    private readonly MeshBufferService _bufferService;
    private readonly Camera _camera;

    public int Order => 100; // From IOrderable

    public MeshSubrenderer(GraphicsPipeline graphicsPipeline, MeshBufferService bufferService, Camera camera)
    {
        _graphicsPipeline = graphicsPipeline;
        _bufferService = bufferService;
        _camera = camera;
    }

    public void Render(CommandBuffer commandBuffer, IRenderPass renderPass)
    {
        // Implementation
    }
}
```

### Parent Renderer Orchestration

The parent renderer injects all subrenderers and orders them:

```csharp
public class GeometryPhase : IRenderer<GameRenderContext>
{
    private readonly IReadOnlyList<IGeometrySubrenderer> _subrenderers;
    private readonly GameRenderContextBuffers _buffers;

    // Owned by the renderer so describing the pass every frame allocates nothing
    private readonly Texture[] _gBufferTextures = new Texture[3];
    private readonly ColorTargetSettings[] _gBufferSettings =
        [ColorTargetSettings.Clear, ColorTargetSettings.Clear, ColorTargetSettings.Clear];

    public GeometryPhase(
        IEnumerable<IGeometrySubrenderer> subrenderers,
        GameRenderContextBuffers buffers)
    {
        _subrenderers = subrenderers.OrderBy(r => r.Order).ToList();
        _buffers = buffers;
    }

    public void Render(GameRenderContext renderContext)
    {
        _gBufferTextures[0] = _buffers.AlbedoBuffer.Texture;
        _gBufferTextures[1] = _buffers.NormalBuffer.Texture;
        _gBufferTextures[2] = _buffers.PositionBuffer.Texture;

        using IRenderPass renderPass = renderContext.CommandBuffer.CreateRenderPass(
            _gBufferTextures, _gBufferSettings, null, DepthBufferSettings.Default);

        foreach (IGeometrySubrenderer subrenderer in _subrenderers)
        {
            subrenderer.Render(renderContext.CommandBuffer, renderPass);
        }

        // RenderPass disposed here - all subrenderers have contributed
    }
}
```

**Note:** `IRenderer<T>` is managed by `RenderCoordinator<T>`, which invokes multiple renderers in order. A renderer can represent an application-specific phase such as geometry, lighting, or post-processing.

## Key Points

- **Don't create RenderPass**: Subrenderers receive an existing RenderPass from the parent
- **IOrderable**: Use this interface to control execution order (lower numbers execute first)
- **IEnumerable injection**: Parent renderer can accept `IEnumerable<IYourSubrenderer>` and order them in the constructor
- **Shared render targets**: All subrenderers draw into the same outputs within a single RenderPass
- **One RenderPass disposal**: The parent manages RenderPass lifetime, not the subrenderers

## When to Use

- **Within a renderer**: When a single `IRenderer<T>` needs to compose multiple rendering operations
- **Shared render targets**: Multiple rendering operations need to write to the same G-buffer or output textures
- **Extensible renderer implementation**: Allow easy addition of new rendering contributions without modifying the parent renderer

Note: If you need separate RenderPasses, implement multiple `IRenderer<T>` classes instead.
