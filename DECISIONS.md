# Decisions

Design decisions with the constraints that decided them and their known costs, newest first.

## 2026-09-25: The render path records through ref structs and reuses its upload transfer buffers

`CommandBuffer`, the render contexts, `RenderPassBuilder`, `RenderPass` and `ComputePass` are `ref struct`s. `GpuMemorySystem` writes uploads into transfer buffers it keeps across submissions.

- A render path that allocates every frame is a defect on the Raspberry Pi 5 target (#468).
- Reusing pooled objects instead keeps a stale reference working: code that kept last frame's object would act on the current frame instead of throwing. A `ref struct` cannot outlive the method that holds it.
- A `ref` field cannot point to a `ref struct` (CS9050), so the passes cannot refer to `CommandBuffer`. The pass state lives in a plain struct inside the command buffer, and a pass is a handle to it. SDL allows one open pass per command buffer, so one slot is enough, and a pass number detects a handle kept after its pass ended.
- A `using` local cannot be passed by `ref` (CS1657), and `using (variable)` disposes a copy taken at its start. Passes are therefore handles passed by value, which keeps `using RenderPass` working, and the coordinator disposes the context in `finally`.
- SDL's `cycle = true` would reuse one transfer buffer too, but it adds hidden copies without a cap and never frees them before the buffer. Pixely's slots are reused once their fence signals. The fence is only queried, because waiting suspends the wasm stack in the browser, and the swapchain acquire already limits the submissions in flight.

Cost: a `CommandBuffer` or context copied by value keeps its own state, so submitting both submits the native buffer twice; nothing but documentation prevents the copy. None of these types can be stored in a field, captured by a lambda, used across an `await` or mocked. A render context cannot derive from `BasicRenderContext` and holds one instead. A method taking the command buffer by `ref` and a pass must mark the pass `scoped`. `IRenderPass`, `IComputePass`, `IRenderPassBuilder` and `CommandBuffer.CreateCopyPass` are gone. A window hands out the same `SwapchainTexture` every frame, so one kept from an earlier frame refers to the current frame's texture. The slots can hold up to 8 MiB, and an upload over 1 MiB, one that does not fit a slot already at 1 MiB, a texture, or one made while all 8 slots are busy still creates its own transfer buffer.

## 2026-09-24: Browser native archives come from a URL pinned by SHA-256, not from a NuGet package

A browser app links a prebuilt Emscripten archive, such as `libXDL_wgpu.a` from a GitHub release, with a `NativeUrlReference` that names the URL and the file's SHA-256. The SDK downloads it into a cache in the project's `obj` folder and makes it a `NativeFileReference`.

- The archives are published as release assets. A NuGet package that wraps them would be one more package to build, version and publish for every archive release.
- The hash pins the file as a package version would, and a cached file needs no network.
- The WebAssembly targets choose to relink from the count of `NativeFileReference` items in a target. A Pixely target that runs before it downloads the files and adds the items, so a `NativeUrlReference` from `Directory.Build.targets` or a package's targets counts too.
- The cache is per project. A cache shared across projects needs an atomic replace for builds that download at once, and MSBuild's `Move` deletes the destination first on Linux and macOS.
- URL references go first on the link line, so an archive such as `libXDL_wgpu.a` replaces the functions it defines in a `SDL3.a` that stays a file.

Cost: the first build of each project needs the network, and so does every build after `obj` is deleted, such as on a fresh clone or in CI. An asset of a private repository needs credentials, which the download does not send. A URL with a query string does not work, because `?` is an MSBuild wildcard. The cache path and the file name come from the URL, which is untested on Windows.

## 2026-09-23: The browser page's end tears down the WebGPU device and SDL

In the browser, disposing the app releases Pixely's own GPU resources and windows but calls neither `SDL_DestroyGPUDevice` nor `SDL_Quit`. The tab ends the app, and the browser frees the device, SDL and the wasm memory with the page.

- WebGPU requests and completes work only through promises, and SDL's WebGPU backend can wait for them only by suspending the wasm stack.
- Asyncify is ruled out: the .NET browser runtime is not built with it, and it slows the whole module.
- JSPI is too recent to require: Firefox 153 (July 2026) and Safari 27 (September 2026) were the last to ship it.
- A clean destroy has to wait for the last submissions, which a synchronous `Dispose` cannot do. Awaiting the queue before `Dispose` covered only the paths that awaited first.
- The device itself is still requested by the page and adopted by SDL through the WebGPU backend's custom create properties, before `Build()`.

Cost: GPU memory stays allocated after the app ends until the page is left, including while the default page shows its stop message and possibly while the page sits in the back/forward cache, which WebGPU does not block in Chromium. A second app in the same page is unsupported.

Sources: [Emscripten `EXIT_RUNTIME`](https://emscripten.org/docs/tools_reference/settings_reference.html), [WebGPU Explainer](https://gpuweb.github.io/gpuweb/explainer/), [JSPI support](https://caniuse.com/wf-wasm-jspi), [Chromium bfcache blocking features](https://chromium.googlesource.com/chromium/src/+/refs/heads/main/third_party/blink/public/mojom/scheduler/web_scheduler_tracked_feature.mojom).
