# Testing renderers without a GPU

`Pixely.Testing` gives a game's tests a GPU device without a GPU. `StubGpuDevice` is a `GpuDevice`: the game's renderers, `GpuMemorySystem`, the shader and texture loaders, `GraphicsPipelineBuilder`, `RenderPassBuilder` and Pixely.Ui run on it unchanged, Pixely's validation runs as on the GPU, and the stub records what reaches it. Nothing is rendered. Under [peach-architecture.md](peach-architecture.md) `Foo.Game` knows no output, so its tests need none of this; it is for `Foo.Frontend.Rendering` and the frontend code that renders.

## The app

`UseStubGpu()` registers the stub as the app's `GpuDevice`, with every GPU service on top of it. Call it before `UseGpu`, `UseDefaultRendering` or a registrar that calls them, which then keep the stub; after another device it throws. Every window is a `StubWindow`: an SDL window created as its `WindowConfig` says, without a swapchain. Each frame it hands out a new `SwapchainTexture` of its size at once, with no wait, so a frame submits only its own command buffer. Its events, size and visibility work as on any window, and a hidden window is not renderable. Building still initializes SDL video, so the app picks SDL's dummy video driver at default priority, which `SDL_VIDEO_DRIVER` in the environment overrides; with another driver the windows show. The choice lasts as long as the app, so another app in the same test process, on the GPU or not, is unaffected.

Headless mode ([headless.md](headless.md)) runs the game on the GPU to read its frames back, so building an app with both it and the stub throws `PixelyInitializationException`, whether `PixelyConfig.Headless` or `PIXELY_HEADLESS` asks for it.

```csharp
PixelyAppBuilder builder = new();
builder.UseDefaultContent();
builder.UseStubGpu();
builder.AddFooRendering();   // the game's own registrar, which calls UseDefaultRendering
using IPixelyApp app = builder.Build();

app.RunFrame();

StubGpuDevice device = app.GetRequiredService<StubGpuDevice>();
StubCommandBuffer frame = device.SubmittedCommandBuffers.Single(commandBuffer => commandBuffer.RenderPasses.Count > 0);
StubDraw draw = frame.RenderPasses.Single().Draws.Single();
Assert.That(draw.Pipeline, Is.SameAs(spriteRenderer.Pipeline));
Assert.That(draw.FragmentSamplers[0].Texture, Is.SameAs(spriteRenderer.Atlas));
Assert.That(draw.ReadVertexUniform<Matrix4x4>(0), Is.EqualTo(expectedCamera));
```

Shaders load from the content as in the game, so the test project needs the compiled shaders, metadata included. The stub reads the metadata, whose binding layout the validation checks draws against, and supports every shader format, so it never reads the shader code.

A renderer can also be called without a frame: `StubGpuDevice.CreateSwapchainTexture(size)` makes the target of a `BasicRenderContext`, and `device.AcquireCommandBuffer()` returns a `StubCommandBuffer` to record into and submit.

## What is recorded

- `SubmittedCommandBuffers`: every submitted `StubCommandBuffer`, oldest first, until `ClearSubmittedCommandBuffers()`. Besides the frames this includes the command buffers `GpuMemorySystem` submits its uploads with. A cancelled command buffer is not submitted; it has `IsCancelled`.
- A `StubCommandBuffer` holds its `RenderPasses`, `ComputePasses` and `Blits` in recording order.
- A `StubRenderPass` holds its `ColorTargets`, `ColorTargetSettings`, `ResolveTextures` (one per color target, null for none), `DepthBuffer`, `DepthBufferSettings` and `Draws`.
- A `StubDraw` holds the state current when it was recorded: the pipeline, the vertex buffers, sampler bindings and storage buffers by slot, the index buffer of an indexed draw, the vertex, index and instance counts, the scissor clipped to the pass's target (null when the pass never set one) and the stencil reference. `ReadVertexUniform<T>(slot)` and `ReadFragmentUniform<T>(slot)` return the value pushed last before the draw and throw when nothing was pushed or its size is not that of `T`. A slot bound earlier in the pass and not bound again keeps its binding, as on the GPU.
- A `StubComputePass` holds its read-write bindings and `Dispatches`; a `StubDispatch` holds the pipeline, the group counts or the indirect buffer, the bindings by slot, and `ReadUniform<T>(slot)`.

Only calls that pass Pixely's validation are recorded, and a call that fails it throws as on the GPU: a draw whose pipeline's shader expects a uniform the frame never pushed throws `ShaderBindingLayoutValidationException`, a sample count mismatch throws `InvalidOperationException`.

## Contents

The stub keeps what is uploaded. An upload reaches the contents when its command buffer is submitted, which `GpuMemorySystem.Submit()` and every frame do, and is dropped when it is cancelled. `ReadVertexBuffer`, `ReadStorageBuffer` and `ReadIndexBuffer` return the buffer's current `Size` elements from its start, 16-bit indices widened to `uint`. `ReadTexture(texture, layer)` returns the layer's uploaded bytes, or zeros for a layer nothing was uploaded to, such as a color target; `SubmitAndDownloadTexture` returns the same. Reading a released resource throws `ObjectDisposedException`.

## Options

`StubGpuDevice.Create(options)` and `UseStubGpu(options)` take a `StubGpuDeviceOptions`. By default the stub supports every shader format, texture format and sample count; `ShaderFormats`, `IsTextureFormatSupported` and `IsSampleCountSupported` narrow them, for a test of a fallback.

## What the stub cannot tell

It runs no shader, so it cannot show what a frame looks like or catch a shader bug, SDL's own validation or a driver's. It does not check a texture's usage against its binding, which Pixely does not check either. Its fences are signalled at once. It runs on the desktop build of Pixely only, not the browser one.
