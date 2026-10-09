# Multisampling

Multisample anti-aliasing (MSAA) stores several samples per pixel. A triangle covers each sample on its own, so a pixel on an edge ends up partly covered. A resolve then averages the samples into a texture with one sample, which blends edge pixels by how much of them the geometry covers.

The tutorial `tutorials/Pixely.Tutorials.Multisampling` draws a triangle with four samples and resolves it into the swapchain texture.

## Sample counts

`SampleCount` has `Count1`, `Count2`, `Count4` and `Count8`. `Count1` is no multisampling and always works. Support for the others depends on the backend and the texture format, so check it with `GpuDevice.IsSampleCountSupported(format, sampleCount)`:

| Backend | What SDL checks |
|---|---|
| Vulkan | The device limits `framebufferColorSampleCounts` for color formats and `framebufferDepthSampleCounts` for depth formats. Vulkan requires both to include 1 and 4. |
| Direct3D 12 | `CheckFeatureSupport` with `D3D12_FEATURE_MULTISAMPLE_QUALITY_LEVELS` for the format and count. The SDL that Pixely pins checks a depth format by its shader-view format, so it can report no multisampling for a depth format the GPU supports it for, such as `Depth24Stencil8` ([libsdl-org/SDL#16124](https://github.com/libsdl-org/SDL/issues/16124), fixed in a later SDL). |
| Metal | The device's `supportsTextureSampleCount`, whatever the format. |
| WebGPU | Only the count: 2 and 8 are unsupported, 4 is reported as supported for every format. WebGPU itself allows 4 only for formats with the multisampling capability, so a format without it passes the check and fails when the texture is created ([stanoddly/XDL_wgpu#24](https://github.com/stanoddly/XDL_wgpu/issues/24)). |

`Count4` is the count to start with: Vulkan requires it, and it is the only count above one that WebGPU has.

Texture creation and `GraphicsPipelineBuilder.Build()` throw for a count the device does not support with the format.

## Textures

Pass the count when the texture is created:

```csharp
Texture colorTarget = gpuDevice.CreateColorTargetTexture(size, format, SampleCount.Count4);
Texture depthBuffer = gpuDevice.CreateDepthBufferTexture(size, DepthBufferFormat.Depth16, sampleCount: SampleCount.Count4);
```

`GpuDevice.CreateTexture(size, format, usage, sampleCount)` takes it too. `Texture.SampleCount` reports it. An undefined `SampleCount` value throws `ArgumentOutOfRangeException`.

- A multisampled texture can only be a color or depth-stencil target. It cannot be sampled, read or written as storage, or downloaded with `CommandBuffer.SubmitAndDownloadTexture`. Its samples reach a shader only through a resolve.
- So `CreateColorTargetTexture` with a count above `Count1` creates a color target without the sampler usage, and `CreateDepthBufferTexture` throws with `sampler: true`.
- Every sample takes memory, so `GpuDevice.MemoryStats` counts a four-sample texture as four times its single-sample size.

## Render passes

Every attachment of a pass has the same sample count: the color targets and the depth-stencil buffer. A pass with a multisampled color target and depth testing therefore needs a depth-stencil buffer with the same count. `Build()` throws on a mismatch.

A multisampled color target names its resolve texture when it is added, and its settings resolve into it:

```csharp
ColorTargetSettings resolveSettings = new() { StoreOperation = StoreOperation.Resolve };

using RenderPass renderPass = new RenderPassBuilder(commandBuffer)
    .AddColorTarget(colorTarget, renderContext.SwapchainTexture, resolveSettings)
    .SetDepthBuffer(depthBuffer, new DepthBufferSettings { DepthBufferStoreOperation = StoreOperation.DontCare })
    .Build();
```

- `StoreOperation.Resolve` writes the resolved pixels and discards the samples. `StoreOperation.ResolveAndStore` also keeps the samples, for a later pass that loads them with `LoadOperation.Load`.
- `AddColorTarget(texture, resolveTexture)` without settings takes the shared settings from `SetSharedColorTargetSettings`, like `AddColorTarget(texture)`.
- The resolve texture has one sample, the color target's format and its size, and the usage `TextureUsage.ColorTarget`. `Texture.Usage` reports a texture's usage. Resolving into the swapchain texture therefore needs a color target in the swapchain texture's format, created again when the window's size changes.
- `Build()` throws when a target has a resolve texture but its store operation does not resolve, when the store operation resolves but there is no resolve texture, and when the textures break the rules above.
- A depth-stencil buffer cannot be resolved. SDL has no resolve for it, so its store operations stay `Store` or `DontCare`, and `Build()` throws for the others. A pass that only needs depth for testing uses `DontCare`.

`CommandBuffer.CreateRenderPass` takes the resolve textures as a span with one entry per color target, null for a target that does not resolve, or as an empty span when nothing resolves.

## Pipelines

A pipeline that draws into multisampled targets has their count:

```csharp
GraphicsPipeline pipeline = graphicsPipelineBuilder
    .AddVertexBufferConfig<PositionVertex>()
    .SetShaderProgram("shaders/shader")
    .AddColorFormatFromDisplay()
    .EnableDepthTesting(DepthBufferFormat.Depth16)
    .EnableMultiSampling(SampleCount.Count4)
    .Build();
```

`RenderPass.BindGraphicsPipeline` throws when the pipeline's `SampleCount` differs from the pass's `SampleCount`. Leave the `mask` argument of `EnableMultiSampling` null: SDL reserves the sample mask for future use.
