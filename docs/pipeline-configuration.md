# Pipeline Configuration

Quick reference for `GraphicsPipelineBuilder` methods.

## Vertex Configuration

```csharp
.AddVertexBufferConfig<PositionVertex>()
.AddVertexBufferConfig<PositionNormalColorVertex>()
```

Add for each vertex buffer type you'll bind. Order matters - matches the binding order in your shader and the order you call `BindVertexBuffer()`.

**Available vertex types:**
- `PositionVertex` - Position only
- `PositionColorVertex` - Position and color
- `PositionTextureVertex` - Position and texture coordinates
- `PositionNormalColorVertex` - Position, normal, and color
- `PositionTextureNormalVertex` - Position, texture coordinates, and normal
- `PositionTextureNormalColorVertex` - Position, texture coordinates, normal, and color

## Primitive Types

```csharp
.SetPrimitiveType(PrimitiveType.TriangleList)
.SetPrimitiveType(PrimitiveType.TriangleStrip)
```

**TriangleList**: Every 3 vertices = 1 triangle. Most common, works with indexed drawing.
**TriangleStrip**: First 3 vertices make triangle, each additional vertex adds a triangle. Good for quads with 4 vertices.

## Shader Loading

```csharp
// Option 1: Direct path
.SetShaderProgram("shaders/shader")

// Option 2: Load once and reuse across pipeline configurations
GraphicsShaderProgram shaderProgram = shaderLoader.LoadGraphicsShaderProgram("shaders/terrain");
.SetShaderProgram(shaderProgram)
```

## Color Targets

```csharp
// Single target (forward rendering)
.AddColorFormatFromDisplay()  // Match the default window's swapchain format

// Multiple targets (deferred rendering)
.AddColorTarget(renderContextBuffers.AlbedoBuffer.Format)
.AddColorTarget(renderContextBuffers.NormalBuffer.Format)
.AddColorTarget(renderContextBuffers.PositionBuffer.Format)
```

Add color targets in the order they appear in fragment shader outputs. Each `AddColorTarget()` call adds one output.

## Depth Testing

```csharp
.EnableDepthTesting(depthFormat, writeDepth: true, compareOp: CompareOperation.ReversedLess)
```

**Parameters:**
- `depthFormat`: Get from depth buffer format
- `writeDepth`: `true` for geometry, `false` for transparent/overlay
- `compareOp`:
  - `CompareOperation.Less` - Traditional depth testing (near = 0, far = 1)
  - `CompareOperation.ReversedLess` - Reverse-Z (near = 1, far = 0, better precision)

**Use Reverse-Z** for significantly better depth precision across all depth ranges.

## Complete Example

```csharp
GraphicsPipeline pipeline = graphicsPipelineBuilder
    .SetPrimitiveType(PrimitiveType.TriangleList)
    .AddVertexBufferConfig<PositionNormalColorVertex>()
    .SetShaderProgram("shaders/terrain")
    .AddColorTarget(renderContextBuffers.AlbedoBuffer.Format)
    .AddColorTarget(renderContextBuffers.NormalBuffer.Format)
    .AddColorTarget(renderContextBuffers.PositionBuffer.Format)
    .EnableDepthTesting(renderContextBuffers.DepthBuffer.Format, true, CompareOperation.ReversedLess)
    .Build();
```

## Notes

- Call `.Build()` last to create the pipeline
- Pipeline is immutable after build
- GraphicsPipelineBuilder is injected as a dependency once rendering or `UseGpu()` is registered (`docs/window-rendering.md`)
- Store pipelines, don't rebuild every frame
