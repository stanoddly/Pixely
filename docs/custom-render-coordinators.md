# Custom render coordinators

`RenderCoordinator<TRenderContext>`, which `UseWindowRendering` and `UseDefaultRendering` register, runs each window's frame: it acquires the command buffer and the swapchain texture, has the provider build the context, runs the renderers and submits. A custom render context or provider changes what is drawn without touching that sequence (see [Window rendering](window-rendering.md)). Write a custom `IRenderCoordinator` only when the sequence itself has to change, such as a different acquire policy, and follow the rules below. `RenderCoordinator<TRenderContext>` follows all of them, so its source is the reference implementation.

## Registration and the frame loop

A coordinator is an `IRenderCoordinator` singleton. The frame loop calls `Execute()` once per frame for every registered coordinator, after the updates. Coordinators registered in a stage join the loop while the stage is loaded and leave it when the stage is unloaded.

`Execute()` returns whether the window could show the frame: it was renderable and a swapchain texture came back, even if nothing was drawn. When every coordinator returns false, the desktop frame loop waits up to 16 ms for an event, so an app whose windows are all hidden or minimized does not spin. The browser does not wait: `requestAnimationFrame` paces it.

Only one coordinator may acquire a given window's swapchain texture per frame. Two would present the window twice.

## SDL's rules for the frame

`RenderCoordinator<TRenderContext>` keeps these by acquiring, drawing and submitting on one command buffer, on the frame loop's thread, for one window.

- Draw into the swapchain texture only on the command buffer that acquired it. The acquire attaches the texture's synchronization and presentation to that command buffer, and SDL says the texture "should only be referenced by the command buffer used to acquire it" (`SDL_AcquireGPUSwapchainTexture` in `SDL_gpu.h`). On Vulkan, another command buffer would access the image without waiting for it to be acquired.
- Use one GPU device for the frame: the window must be claimed for the device the command buffer comes from, and the uploads must come from that device's `GpuMemorySystem`. SDL acquires through the command buffer's device and does not check which device claimed the window, so a stage's coordinator that pairs its own device with a root window passes one device's swapchain to another.
- Acquire the swapchain texture on the thread that created the window, and submit a command buffer on the thread that acquired it (`SDL_AcquireGPUSwapchainTexture` and `SDL_SubmitGPUCommandBuffer` in `SDL_gpu.h`). Pixely runs the frame loop, and with it every coordinator, on that one thread.
- Draw nothing when the acquire returns no texture, even while `Window.IsRenderable` is true. SDL returns no texture for a hidden window, while too many frames are in flight, and after Vulkan loses the window's surface, among other cases.

## Submit within the frame

A command buffer that acquired a swapchain texture must be submitted before `Execute()` returns, and never kept for a later frame. SDL does not allow cancelling it once a texture was acquired on it.

The frame loop relies on this. On the desktop, after a frame in which no window of a GPU device requested a swapchain texture, it releases every window claimed for that device, and SDL frees its data for each released window. A command buffer that acquired a texture in an earlier frame and is submitted later would read that freed data.

If the acquire throws, cancel the command buffer: nothing was acquired on it yet.

## Uploads go first

Submit `GpuMemorySystem` right before the frame's own command buffer, on every path that submits the frame, including when the window, the provider or a renderer throws. Buffer updates cycle the buffer into a new copy, and the upload that fills the copy runs only when `GpuMemorySystem` is submitted. A draw that reads an updated buffer from a command buffer submitted before the uploads reads undefined contents.

Submit the frame even when submitting the uploads throws, so an acquired swapchain texture is not abandoned.

## Request a texture every frame on the desktop

On the desktop, call `TryWaitAndAcquireSwapchainTexture` every frame, also for a window that is hidden or minimized, and submit the command buffer even when no texture comes back. Run the renderers only while `Window.IsRenderable` is true.

- SDL's Vulkan backend frees finished GPU work, buffer copies included, only on a submit whose command buffer requested a swapchain texture, on a fence wait, or while no window is claimed. A request counts even when no texture comes back.
- A frame in which a device's windows request nothing makes the frame loop release them, which waits for the device to go idle, and the next acquire claims each window again and recreates its swapchain. A coordinator that requests only on some frames would pay that on every switch.
- An unclaimed minimized window is the exception: `TryWaitAndAcquireSwapchainTexture` returns false without requesting anything and without claiming it, because SDL's Vulkan backend on some drivers, such as NVIDIA on Win32, reports a claim of a minimized window as successful without claiming it.

## The browser

- Skip a window that is not renderable instead of acquiring for it, but still submit `GpuMemorySystem`. SDL's browser driver does not hide the canvas, so presenting an undrawn texture would blank it, and the WebGPU backend frees finished work on every submit without a request.
- When no swapchain texture comes back, cancel the command buffer and leave the uploads pending for the next drawn frame. The WebGPU backend returns no texture while its submissions in flight reach its frame limit of 2, counting empty ones, so another submission would hold a slot until the frame ahead of it finishes.
- The browser never releases window claims.

## Render contexts

A coordinator that builds an `IRenderContext` disposes it before it submits. A context or a renderer must not submit or cancel the command buffer: the coordinator owns it.
