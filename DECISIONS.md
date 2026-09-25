# Decisions

Design decisions with the constraints that decided them and their known costs, newest first.

## 2026-09-25: Frame objects are reused like pooled arrays, except under GPU validation

Command buffers come from a pool on `GpuDevice` and go back on submit or cancel. Each keeps one render pass, one compute pass and one pass builder for reuse. `BasicRenderContextProvider` reuses its context and each window its `SwapchainTexture`. `GpuMemorySystem` writes uploads into transfer buffers it keeps across submissions.

- A render path that allocates every frame is a defect on the Raspberry Pi 5 target (#468).
- `ref struct`s instead (#585) removed the allocations too, but a copy of a command buffer or context silently kept its own state, and consumers had to pass them by `ref`, mark passes `scoped` and stop deriving from `BasicRenderContext`. Reusing classes keeps today's API and calling code.
- Using a reused object after it went back is a programming error, as it is for an array from `ArrayPool`. A version number cannot detect it, because the stale holder and the new one share the object.
- GPU validation already marks a debugging session, so with it on nothing is reused and a stale object throws `ObjectDisposedException` as before.
- Upload slots are reused once their fence signals. The fence is only queried, because waiting suspends the wasm stack in the browser. SDL's `cycle = true` would add hidden copies without a cap instead.

Cost: without validation, a command buffer, pass or context kept past its frame records into a later frame's work instead of throwing, and disposing a command buffer after submitting it cancels whoever holds it next. A context derived from `BasicRenderContext`, like any custom context, is still allocated every frame unless its provider reuses it. The upload slots can hold up to 8 MiB, and an upload over 1 MiB, a texture, or one made while all 8 slots are busy still creates its own transfer buffer.

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
