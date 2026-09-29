# Decisions

Design decisions with the constraints that decided them and their known costs, newest first.

## 2026-09-28: Headless apps run in lockstep with their input, on a fixed step, and automation commands end with `;`

In headless mode game time starts at 0 and advances by 1/30 second per frame. Frames run only through `wait N`, unpaced, and when no command is left the frame loop blocks on standard input until the next `;`. The end of the input quits the app. Every automation command ends with `;`, and a newline is ordinary whitespace. `OffscreenWindow` waits for all earlier GPU work each time its texture is acquired.

- Determinism needs the frame loop to advance on the script's text, not on when input arrives. With frames on the real clock, a command lands in whichever frame is running when it is read. A reader thread made that a race, and a non-blocking read on the frame loop would have kept the same race.
- A time scale on the real clock stops working above about 3x: `TimeDelta` is capped at 0.1 second, and deltas that large break physics. A fixed step gives the game the same frame times however fast frames run.
- Starting at 0 gives every run of a scenario the same `ElapsedTime`, so its screenshots can be repeated.
- With a fixed step, a second is always 30 frames. A wait in seconds would add only a conversion and its rounding.
- The two clocks are two `PixelyFrameContext` subclasses picked by `PixelyFactory.CreateFrameContext()`, so the real clock's code has no headless branch.
- Unpaced offscreen frames never acquire a swapchain texture, so nothing limited the GPU work in flight. The wait sits in `OffscreenWindow` because acquiring is where a swapchain window waits for a free frame too, and it keeps headless code out of the frame loop. SDL GPU has one queue per device, so a fence on an empty command buffer covers every earlier submission on Vulkan, D3D12 and Metal.
- `;` lets a whole scenario fit on one shell line. `text` and `screenshot` cannot contain `;`, because an escape rule was not worth its cost.

Cost: nothing happens without a `wait`, and the end of input now quits instead of leaving the app running. A blocked app processes no SDL events. Work that follows the real clock, such as loading on a background thread, can finish in different frames from run to run. Headless frames have no CPU/GPU overlap. The `;` grammar breaks every newline-only script, and `#` comments are gone. The public `OffscreenWindow.FrameInterval` was removed, `PixelyFrameContext` became abstract and its `StartFrame` internal. Synthetic input takes its timestamps from game time, but events from SDL keep SDL's clock, so the two cannot be compared. `PerformanceTracker` reports the fixed step in headless mode. Scenarios count frames, so they would change meaning if the step ever became configurable.

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

- SDL's Vulkan backend frees finished work, released buffers included, only on a submit that acquired a swapchain texture or when no window is claimed. A claimed offscreen window never acquires one, so a headless run kept every released buffer until a screenshot waited on a fence.
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
