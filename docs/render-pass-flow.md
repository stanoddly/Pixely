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

## Reused objects

A frame allocates no command buffer, pass, pass builder, basic render context or swapchain texture: Pixely hands out the same
objects again. `AcquireCommandBuffer` takes a command buffer from a pool on the device, and `Submit`, `SubmitAndAcquireFence`
and `Cancel` return it. A command buffer reuses one render pass, one compute pass and one builder from
`CreateRenderPassBuilder()`.

This is the contract of an array from `ArrayPool`: an object must not be used after it went back.

- Do not keep a command buffer, pass or context past the frame, and do not dispose a command buffer after submitting it.
  The next frame may already hold the same object, and a stale reference records into that frame's work instead of throwing.
- Build a builder from `CreateRenderPassBuilder()` before asking for another one on the same command buffer.
- A command buffer allows one open pass at a time. Beginning another before disposing the first throws.
- `new RenderPassBuilder(commandBuffer)` still works, but allocates a builder every time.

With GPU validation on (`PixelyConfig.EnableGpuValidation`, `PIXELY_GPU_VALIDATION=1`), nothing is reused: every call
returns a new object, and one used after its command buffer was submitted, or after its pass was disposed, throws
`ObjectDisposedException`. Turn validation on to find misuse. A swapchain texture is reused either way, since one kept from
an earlier frame simply refers to the current frame's texture.

## Execution Model

### Pattern 1: Create Own RenderPass

```csharp
public void Render(BasicRenderContext renderContext)
{
    // 1. BEFORE RenderPass: Push uniforms that need to be outside the pass
    renderContext.CommandBuffer.PushFragmentUniformData(0, color);

    // 2. CREATE RenderPass
    using IRenderPass renderPass = renderContext.CommandBuffer.CreateRenderPassBuilder()
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
commandBuffer.CreateRenderPassBuilder()
    .AddColorTarget(texture)                              // Output texture
    .SetSharedColorTargetSettings(ColorTargetSettings.Clear)  // Clear on start
    .Build()
```

**ColorTargetSettings options:**
- `Clear` - Clear the target before rendering
- `Load` - Keep existing contents
- Others may exist for different load/store operations

Add multiple color targets for deferred rendering (G-buffer).

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
