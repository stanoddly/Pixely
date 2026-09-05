# Render Pass Flow

## Two Key Objects

### CommandBuffer
Records GPU commands. Lives for the entire frame. Used for:
- Pushing uniform data (push constants)
- Creating RenderPasses

### RenderPass
Active rendering context. Created from CommandBuffer. Used for:
- Binding pipelines
- Binding vertex buffers
- Drawing primitives
- **Disposed to execute** - rendering happens on dispose

## Execution Model

### Pattern 1: Create Own RenderPass

```csharp
public void Render(BasicRenderContext renderContext)
{
    // 1. BEFORE RenderPass: Push uniforms that need to be outside the pass
    renderContext.CommandBuffer.PushFragmentUniformData(0, color);

    // 2. CREATE RenderPass
    using IRenderPass renderPass = renderContext.CommandBuffer.CreateRenderPass(
        renderContext.SwapchainTexture, ColorTargetSettings.Clear);

    // 3. INSIDE RenderPass: Bind and draw
    renderPass.BindGraphicsPipeline(_graphicsPipeline);
    renderPass.BindVertexBuffer(_vertexBuffer);
    renderPass.DrawPrimitive();

    // 4. RenderPass disposed here - commands execute
}
```

### Pattern 2: Receive Existing RenderPass

Used by subrenderers that contribute to a larger multi-phase rendering pipeline (like deferred rendering). The parent system creates the RenderPass and calls multiple subrenderers that all draw into the same render targets.

```csharp
public void Render(CommandBuffer commandBuffer, IRenderPass renderPass)
{
    // RenderPass already exists, don't create a new one

    // 1. Push uniforms using CommandBuffer
    commandBuffer.PushVertexUniformData(0, viewProjection);

    // 2. Bind pipeline
    renderPass.BindGraphicsPipeline(_graphicsPipeline);

    // 3. Draw loop
    foreach (var item in items)
    {
        renderPass.BindVertexBuffer(item.Buffer);
        commandBuffer.PushVertexUniformData(1, item.WorldMatrix);
        renderPass.DrawPrimitive();
    }

    // DON'T dispose RenderPass - caller manages it
}
```

## Push Constants (Uniforms)

Push constants send small amounts of data to shaders.

```csharp
// Vertex shader uniforms - typically matrices
commandBuffer.PushVertexUniformData(0, viewProjectionMatrix);
commandBuffer.PushVertexUniformData(1, worldMatrix);

// Fragment shader uniforms - typically colors, parameters
commandBuffer.PushFragmentUniformData(0, color);
```

**Slot numbers (0, 1, 2...)** must match your shader uniform bindings.

## Binding Order

Typical order inside a RenderPass:

1. **BindGraphicsPipeline** - Sets the pipeline state
2. **BindVertexBuffer** - Binds vertex data
3. **PushUniformData** - Update per-draw data (optional)
4. **DrawPrimitive** - Issues the draw call (must be last)

**DrawPrimitive must come last.** The order of the other calls is generally flexible, though binding the pipeline first is a common pattern.

For multiple objects, rebind vertex buffers and push new uniforms between draws.

## Creating a RenderPass

`CommandBuffer.CreateRenderPass` takes the pass description directly. It allocates nothing, so it is
what a renderer should call every frame:

```csharp
// One color target
using IRenderPass pass = commandBuffer.CreateRenderPass(texture, ColorTargetSettings.Clear);

// One color target and a depth buffer
using IRenderPass pass = commandBuffer.CreateRenderPass(
    texture, ColorTargetSettings.Clear, depthBuffer, DepthBufferSettings.Default);

// Depth only, no color target
using IRenderPass pass = commandBuffer.CreateDepthOnlyRenderPass(depthBuffer, DepthBufferSettings.Default);

// Several color targets for deferred rendering (G-buffer), from storage the caller owns
using IRenderPass pass = commandBuffer.CreateRenderPass(
    _gBufferTextures, _gBufferSettings, _depthBuffer, DepthBufferSettings.Default);
```

The span overload takes one settings entry per color target, and at most `CommandBuffer.MaxColorTargets`
(8, the point at which SDL itself rejects the pass) targets.

**ColorTargetSettings options:**
- `Clear` - Clear the target before rendering
- `Load` - Keep existing contents
- Others may exist for different load/store operations

## RenderPassBuilder

`RenderPassBuilder` collects the same description across several statements, for a pass composed
conditionally or from a varying number of targets:

```csharp
RenderPassBuilder builder = new RenderPassBuilder(commandBuffer)
    .AddColorTarget(_albedo)
    .SetSharedColorTargetSettings(ColorTargetSettings.Clear);

if (_depthEnabled)
{
    builder.SetDepthBuffer(_depthBuffer, DepthBufferSettings.Default);
}

using IRenderPass pass = builder.Build();
```

It is a class and allocates, so prefer `CreateRenderPass` in a per-frame render path. `Build()` resets
the builder, which can then describe the next pass. Either give every color target its own settings,
or set shared settings for all of them - mixing the two throws.

## Common Patterns

### Single Draw
```csharp
renderPass.BindGraphicsPipeline(pipeline);
renderPass.BindVertexBuffer(buffer);
renderPass.DrawPrimitive();
```

### Multiple Objects (same pipeline)
```csharp
renderPass.BindGraphicsPipeline(pipeline);

foreach (var obj in objects)
{
    renderPass.BindVertexBuffer(obj.Buffer);
    commandBuffer.PushVertexUniformData(1, obj.Transform);
    renderPass.DrawPrimitive();
}
```

### Multiple Pipelines (avoid if possible)
```csharp
// First pipeline
renderPass.BindGraphicsPipeline(pipeline1);
// ... bind buffers and draw ...

// Switch pipeline (expensive)
renderPass.BindGraphicsPipeline(pipeline2);
// ... bind buffers and draw ...
```

## Key Insights

- Push constants can be called before or during RenderPass
- RenderPass disposal triggers actual GPU work
- One RenderPass can have many draw calls
- Changing pipelines mid-pass is valid but expensive
