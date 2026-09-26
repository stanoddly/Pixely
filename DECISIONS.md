# Decisions

Design decisions with the constraints that decided them and their known costs, newest first.

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
