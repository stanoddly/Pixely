# Decisions

Design decisions with the constraints that decided them and their known costs, newest first.

## 2026-10-02: Brotli decompression in the page is opt-in

With `PixelyBrowserBrotli=true`, the default `main.js` installs a resource loader that fetches the `.br` copy of `dotnet.native.wasm` and of each assembly and decompresses it in the page. The page uses `DecompressionStream` where it decodes Brotli and google/brotli's JavaScript decoder elsewhere. Without the property nothing changes.

- GitHub Pages sets no custom headers. It compresses `.wasm` with gzip on the fly and serves `.br` files as opaque files, so the browser never uses the Brotli copies a publish writes.
- Brotli makes the ManyWorlds `_framework/` download 22% smaller than gzip (2.1 MB against 2.6 MB), and 30% smaller with `RunAOTCompilation` (3.6 MB against 5.1 MB).
- Chrome's `DecompressionStream` has no Brotli, so a decoder in JavaScript is the only way there. It costs 67 KB gzipped, loaded only where it is needed.
- On a host that sends `Content-Encoding: br` itself, the loader saves nothing and makes Chrome slower, so it is off by default.
- `main.js` is a static file and cannot read MSBuild properties, so the SDK writes `pixely-options.js` at build time.
- A missing `.br` file falls back to the original, so a build that was not published keeps working.
- The decoder is vendored, not loaded from a CDN, so a game does not depend on a third-party host at run time.

Cost: the decompressed files skip the runtime's integrity check. On Chrome, `dotnet.native.wasm` compiles only after it has fully arrived and been decoded, about 220 ms for a 13 MB AOT runtime. A build without `.br` files costs one failed request per file before the original, and the browser logs each one as a console error. Every page requests `pixely-options.js`, with or without the property.

## 2026-10-02: Browser content is a project-owned archive in the wasm file system

A browser app ships `Content.pk3` as a `PixelyBrowserVfsFile`: a static web asset that the runtime downloads and writes to its in-memory file system below `/` before `Main`. `AddDefaultContent` and `AddZip` load it unchanged, so `UseDefaultContent()` works on both hosts.

- The WebAssembly SDK's `WasmFilesToIncludeInFileSystem` only tags files that are already static web assets, and only in the build. The nested publish of a relinked runtime and a publish without build skip that step and reload the build manifest. So Pixely defines the asset and tags it itself, and also lists it in that item, because only the SDK's step reaches the build boot config.
- Static web assets are resolved before `CopyFilesToOutputDirectory`, so the archive is zipped from the project's content tree. The Pixely target depends on `Compile`, so generated shaders exist however the target is reached.
- The project packages the archive, as on the desktop: content producers do not choose a distribution, and the consuming project does.

Cost: the whole archive downloads into memory before `Main` starts, so its size is startup time and memory, and nothing streams. A browser build zips and copies the archive on every build, because incremental inputs cannot see a deleted file. Two target paths cannot differ only in case, although the file system tells them apart: MSBuild batches item metadata ignoring case, and both the SDK's step and Pixely's tagging batch on the target path.

## 2026-09-30: Every frame requests a swapchain texture, and a frame no window draws waits

On the desktop, `RenderCoordinator` acquires a command buffer and a swapchain texture every frame, even for a hidden or minimized window, and runs renderers only while `Window.IsRenderable` is true. In the browser it skips a window that is not renderable, since SDL's browser driver does not hide the canvas. When no texture comes back, it submits the command buffer instead of cancelling it, except in the browser, where the WebGPU fork returns no texture while its submissions in flight reach the frame limit and a submission would only hold a slot. The coordinator also submits the command buffer after disposing the context, so no provider or context has to request the texture or submit; providers, contexts and renderers must not submit or cancel the command buffer, and a different acquire needs a custom `IRenderCoordinator`. Providers are called only for a frame that is drawn, so a frame nobody sees is submitted without a context. When no window draws a frame, `PixelyApp.RunFrame` waits up to 16 ms for an SDL event, except in the browser.

- SDL's Vulkan backend frees finished GPU work only on a submit that requested a swapchain texture, or on a fence wait. Skipping the request kept every uploaded buffer in use, so each update cycled it into a new copy. Metal and D3D12 free finished work on every submit.
- Requesting is what SDL's own usage assumes, and its 2025-07-31 fix for hidden windows relies on it. The alternative was a fence wait in the frame loop whenever no window rendered, which blocked on the GPU on every backend.
- Apple documents that `nextDrawable`, which SDL's Metal acquire calls for hidden windows too, waits up to one second when no drawable is free. Measured on GitHub's macOS 14.8 and 26.6 runners, on the Apple Paravirtual device, with SDL 3.4.14 and 3.4.16: for a minimized and for a hidden window the acquire never blocked, always returned a texture, and memory stayed flat.
- A hidden or minimized window paces nothing. Vulkan returns no texture at once for a hidden window, and on macOS 14 Metal returned 300 textures in 0.02 s for a minimized one. Without the wait the loop spins and every update records uploads for frames nobody sees. A driver that reports a zero extent for a minimized window, such as NVIDIA on Win32, makes each Vulkan acquire wait for the device to go idle instead (#597).

Cost: on Metal and D3D12, a hidden or minimized window presents an undrawn texture every frame, and so does a minimized Vulkan window whose surface keeps a non-zero size. An app without window rendering must submit `GpuMemorySystem` itself, and with a claimed window it never requests a texture, so on Vulkan it keeps finished work until it waits on a fence. D3D12 was not measured. The wait is a fixed 16 ms, not the display's refresh rate.

## 2026-09-29: Buffer updates cycle the GPU buffer

`CopyPass.UpdateVertexBuffer`, `UpdateIndexBuffer` and `UpdateStorageBuffer` pass `cycle = true` to `SDL_UploadToGPUBuffer`, so an update never overwrites a buffer that an earlier submission still reads.

- SDL tracks which copies are in use, so Pixely needs no fences. On Vulkan this relies on every frame requesting a swapchain texture (2026-09-30).
- A pass keeps the copy it bound, so `RenderPass` takes vertex and index counts at bind time.

Cost: an update replaces the whole contents, not a prefix, and shows in a pass only after the next bind. Each copy is full size and uncounted by the GPU memory tracking. `CopyPass.UploadToBuffer` lists the details.

## 2026-09-28: Headless apps run in lockstep with their input, on a fixed step

In headless mode game time starts at 0 and advances by 1/30 second per frame. Frames run only through `wait N`, as fast as they can, and when no command is left the app blocks on standard input. The end of input quits the app.

- A scenario must give the same frames on every run. That needs the frame loop to advance on the script's text. With frames on the real clock, a command lands in whichever frame is running when it arrives.
- A time scale on the real clock stops working above about 3x, because `TimeDelta` is capped at 0.1 second. A fixed step gives the same frame times at any speed.
- Unpaced frames need a limit on GPU work in flight, so an offscreen window waits for earlier GPU work when its texture is acquired.

Cost: nothing happens without a `wait`. A blocked app processes no SDL events. Work that follows the real clock, such as loading on a background thread, can finish in different frames from run to run.

## 2026-09-27: Small objects that die within a frame are allocated, not pooled

`CommandBuffer`, `RenderPass`, `CopyPass` and `BasicRenderContext` are classes allocated every frame. Small managed objects that nothing references after the frame are allocated rather than pooled.

- A collection of the youngest generation costs time per surviving object, not per dead one. This holds for the .NET GC on desktop and for Mono's SGen in the browser.
- A collection frees dead objects instead of promoting them, so these objects reach an older generation only if a collection happens during their frame.
- A pooled object that a caller stores by mistake comes back in a later frame and acts on that frame's work, a bug far from its cause. A stored `RenderPass` or `CommandBuffer` throws `ObjectDisposedException` instead.
- Pooling render contexts would change the public `RenderContextProvider<TRenderContext>` API, which every custom render context goes through.
- A `ref struct` would make storing them a compile error, but it changes the API more than pooling does.
- Native allocations per frame, such as transfer buffers, stay a defect (#468).

Cost: each allocation still clears memory and brings the next collection closer, and every collection in the browser stops the app. Large objects are not covered: the browser puts objects over about 8 KB in a separate large object space.

Sources: [.NET GC fundamentals](https://learn.microsoft.com/dotnet/standard/garbage-collection/fundamentals), [Mono SGen](https://www.mono-project.com/docs/advanced/garbage-collector/sgen/), [Blazor WebAssembly GC pauses](https://github.com/dotnet/aspnetcore/issues/21085), [wasm large object space fragmentation](https://github.com/dotnet/runtime/issues/118044).

## 2026-09-26: Buffer uploads share one transfer buffer that SDL cycles

`CopyPass` writes a submission's buffer uploads at increasing offsets into one transfer buffer, growing up to 1 MiB. A map at offset 0, the first of each submission or the first into a grown buffer, passes `cycle = true`, and later maps pass `false`.

- A render path that allocates every frame is a defect on the Raspberry Pi 5 target (#468), and a buffer updated every frame created a native transfer buffer every frame.
- With `cycle = true`, SDL hands out a copy of the transfer buffer that no submission in flight still reads, and creates one only when all copies are in use. It tracks that itself: Pixely needs no fences and never waits.
- In the browser, the WebGPU fork checks fences without blocking on every submit, so cycling needs no ASYNCIFY or JSPI. Uploads from the main thread copy from CPU memory when they are recorded, so the browser does not depend on cycling at all.
- Only a map at offset 0 may cycle. The submission's own uploads mark the buffer as in use, so cycling on every map would create a copy per upload. A grown buffer is not in use yet, so SDL does not copy it.
- A Pixely ring of fenced slots did the same with more code: slots, fence queries, and state per submission.

Cost: SDL keeps a copy per submission in flight at the buffer's current size until the buffer is released, so after growing to 1 MiB each copy is 1 MiB. In the browser, every cycling map clears the whole CPU-side buffer, and each copy is a WebGPU buffer the GPU never reads. An upload over 1 MiB, one that does not fit the buffer at 1 MiB, and every texture upload still create their own transfer buffer.

## 2026-09-26: Headless windows are not claimed for the GPU device

`Window` is abstract. `SwapchainWindow` is claimed for the GPU device and presents through its swapchain. `OffscreenWindow`, the window of a headless run, is never claimed and renders into a texture.

- SDL's Vulkan backend frees finished work, released buffers included, only on a submit that requested a swapchain texture or when no window is claimed. A claimed offscreen window never requests one, so a headless run kept every released buffer until a screenshot waited on a fence.
- Each kind of window owns what differs: the swapchain window releases its claim on dispose, and the offscreen window has no claim to release.

Cost: an unclaimed window has no swapchain to report a format, so `OffscreenWindow.ColorTargetFormat` is the fixed `B8G8R8A8Unorm`, the SDR swapchain format of Vulkan and D3D12. A Vulkan driver without it gives a desktop run `R8G8B8A8Unorm` instead, so the two runs then render in different formats. Metal was not checked.

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
