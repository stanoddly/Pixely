# Decisions

Design decisions with the constraints that decided them and their known costs, newest first.

## 2026-09-26: Buffer uploads share one transfer buffer that SDL cycles

`CopyPass` writes a submission's buffer uploads at increasing offsets into one transfer buffer, growing up to 1 MiB. The first map of each submission passes `cycle = true`, and later maps pass `false`.

- A render path that allocates every frame is a defect on the Raspberry Pi 5 target (#468), and a buffer updated every frame created a native transfer buffer every frame.
- With `cycle = true`, SDL hands out a copy of the transfer buffer that no submission in flight still reads, and creates one only when all copies are in use. It tracks that itself: Pixely needs no fences and never waits.
- In the browser, the WebGPU fork checks fences without blocking on every submit, so cycling needs no ASYNCIFY or JSPI. Uploads from the main thread copy from CPU memory when they are recorded, so the browser does not depend on cycling at all.
- Only the first map of a submission may cycle. The submission's own uploads mark the buffer as in use, so cycling on every map would create a copy per upload.
- A Pixely ring of fenced slots did the same with more code: slots, fence queries, and state per submission.

Cost: SDL keeps a copy per submission in flight at the buffer's current size until the buffer is released, so after growing to 1 MiB each copy is 1 MiB. In the browser, every cycling map clears the whole CPU-side buffer, and each copy is a WebGPU buffer the GPU never reads. An upload over 1 MiB, one that does not fit the buffer at 1 MiB, and every texture upload still create their own transfer buffer.

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
