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
    using IRenderPass renderPass = new RenderPassBuilder(renderContext.CommandBuffer)
        .AddColorTarget(renderContext.SwapchainTexture)
        .SetSharedColorTargetSettings(ColorTargetSettings.Clear)
        .Build();

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

## RenderPassBuilder

```csharp
new RenderPassBuilder(commandBuffer)
    .AddColorTarget(texture)                              // Output texture
    .SetSharedColorTargetSettings(ColorTargetSettings.Clear)  // Clear on start
    .Build()
```

**ColorTargetSettings options:**
- `Clear` - Clear the target before rendering
- `Load` - Keep existing contents
- Others may exist for different load/store operations

Add multiple color targets for deferred rendering (G-buffer), up to `RenderPassBuilder.MaxColorTargets` (8, the point at which SDL itself rejects the pass).

`RenderPassBuilder` is a value type with inline storage, so describing a pass every frame allocates nothing.
Each fluent call returns a new value rather than mutating the receiver, so a partly configured builder
can serve as the starting point for several passes. It holds the `CommandBuffer` it was created with,
so a builder value is good for one frame:

```csharp
// Shared configuration, no state shared between the passes built from it
RenderPassBuilder cleared = new RenderPassBuilder(commandBuffer)
    .SetSharedColorTargetSettings(ColorTargetSettings.Clear);

using (IRenderPass albedoPass = cleared.AddColorTarget(_albedo).Build())
{
    // ...
}

using (IRenderPass normalPass = cleared.AddColorTarget(_normals).Build())
{
    // ...
}
```

Either give every color target its own settings, or set shared settings for all of them - mixing the two throws.

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
