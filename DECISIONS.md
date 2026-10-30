# Decisions

Design decisions with the constraints that decided them and their known costs, newest first.

## 2026-10-30: Pixely.Messages keeps two frames of messages, as Bevy does, instead of a log trimmed behind its slowest reader

`Pixely.Observations` becomes `Pixely.Messages`. `Messages<T>` holds the messages of the current and the previous frame and drops the older ones on each update. A `MessageReader<T>` is a transient service whose position is a count of messages, so each consumer that takes one reads on its own. This replaces `ObservationLog<T>` with its named readers, maximum capacity, stall detection and `ObservationSnapshot<T>`.

- Pixely runs every updatable once per frame, and in the Orange architecture systems and updatables read at the same point in every frame, so none falls behind. The log's support for readers several frames behind had no user.
- The names follow Bevy 0.17, which split its events into messages that systems read later and events that observers receive at once. "Observation" would suggest Bevy's observers.
- A save captures the world at `UpdateOrders.Maintenance`, and state that must outlive a frame is a component, so messages need no snapshot.
- A reader no longer registers with the storage, so it needs no name and no `Dispose`.

Cost: a reader that misses messages loses them. That happens when it skips a frame, when it moves its read across Maintenance, or when it reads in a renderer, which Pixely skips while a window is minimized. Its next read throws instead of the writer, and the exception names the message type but not the reader. Messages written in the frame of a save, after one of their readers ran, are not saved. Nothing clears `Messages<T>` on a load.

## 2026-10-25: Pixely.Ecs spawners are classes on Spawner<...>, like queries, instead of [Blueprint] structs

A spawner derives from `Spawner<T1, ..., T8>`, or is written inline as one, like a query on `Query<...>`. `Spawn` takes one value for each type argument, and the tags of the class's `IWith<...>` come without values. A system takes the spawner in its constructor. This supersedes the `[Blueprint]` structs and `Spawner<TBlueprint>` of the 2026-10-23 entry.

- A blueprint needed `partial` and a generator, because C# implements an interface only inside the type. The generic base already knows its component types, so spawners need no generated code, and the blueprint generator and its diagnostics are gone.
- Games create entities through factory methods, as Bevy does with functions that return a bundle. A spawner class holds its factory methods, which call `Spawn`.
- Tags go in `IWith<...>`, as in queries, because a generic `Spawn` cannot drop the parameter of a tag.

Cost: `Spawn` takes up to 8 positional arguments, told apart only by their types. A spawner spawns at least one component with data, so an entity with only tags is created with `Create` and `Add`.

## 2026-10-25: Pixely.Ecs archetypes store each component in one array that doubles, with capacity hints for a known size

Each archetype stores each component that is not a tag in one array, which doubles when full, instead of in chunks of 16 KB. A query reads one span per archetype. `World.EnsureCapacity<TSpawner>` grows a spawner's archetype to a known size, and a spawner's `EnsureRoomFor` makes room for a known number of spawns. This supersedes the chunk storage of the 2026-10-23 entry and the chunk arrays of the table in the 2026-10-24 entry, which now holds each column's array.

- Chunks only help while a column grows: it never copies its rows, and its arrays stay off the large object heap. Their costs apply to every frame. Against chunks, flat columns made `Get` 26 to 27% faster, `UnsafeGet` 30 to 32%, adding and removing a component 21 to 24%, and iteration 3 to 6%.
- Unity's chunks live in native memory, so its reasons do not carry over to the .NET garbage collector. Flecs and Bevy store flat columns.
- Growing one archetype to 1,000,000 entities in frames of 2,000 kept the slowest frame of chunks, about 1.5 ms, and cut the total GC pause from about 2.4 ms to 0.3 ms. It allocated 1.6 times as much, with two background gen 2 collections. With `EnsureCapacity`, the slowest frame was about 0.1 ms and the allocation matched chunks.
- A strategy chosen per world would cost a branch in `Get` or a generic `World<TStorage>` throughout the API. A capacity hint gets the one benefit of chunks without either.

Cost: growing without a hint copies the rows and leaves the old arrays as garbage, and arrays over 85 KB go on the large object heap, so growth from nothing to 100,000 entities triggers gen 2 collections. A column can be up to half empty after doubling, and keeps its size when entities leave. A query can no longer split one archetype into chunks for parallel work; it would split the span instead.

## 2026-10-24: Pixely.Ecs lookups favor speed: an Entity has no world id, and archetypes index column chunks by component id

An `Entity` is an index and a version, 8 bytes. Each archetype keeps the chunk array of each column in a table indexed by component id, which `World.Get<T>` and `UnsafeGet<T>` read through. `UnsafeGet<T>` skips every check of `Get<T>`.

- A world id made `Entity` 12 bytes and cost 2 to 8% on creation and structural changes. Arch 2.1.0 carries one, but its `Get` and `IsAlive` ignore it. `AddEcs` creates one world per app, and child collections reuse it, so only worlds created by hand can mix entities.
- The table replaces the path through the column index, the `Column<T>` object and its chunk shift. Random access by entity went from 1.06 to 0.79–0.88 of Arch's `Get`, which checks neither the version nor the component, and `UnsafeGet` to 0.72–0.81.
- An index whose version reaches `int.MaxValue` is retired, because wrapping the version would revive the oldest handles.

Cost: an entity of another world is not detected: when the world has an entity with the same index and version, the call acts on that entity. The table takes 8 bytes per component id, up to the archetype's highest, in every archetype, and the archetype refreshes it whenever it adds a chunk, because a column replaces its chunk array when it grows. A retired index keeps its record forever. Misusing `UnsafeGet` is undefined behavior.

## 2026-10-23: Pixely.Ecs owns its archetype storage, and the queries and writers a system's constructor takes are its contract

`Pixely.Ecs` is an experimental ECS with its own archetype storage on `Pixely.Collections`, not a wrapper over Arch or another ECS library. Queries are classes that derive from `Query<...>`, whose `Span<T>` and `ReadOnlySpan<T>` type arguments `foreach` deconstructs per chunk, filtered by `IWith<...>` and `IWithout<...>`. A system implements `ISystem`. Its constructor takes its contract, which is queries, `ComponentAdder<T>`, `ComponentRemover<T>`, `EntityDestroyer` and `Spawner<TBlueprint>`, and any services. A `SystemScheduler` runs the systems in registration order and applies their structural changes at sync points.

- A wrapper exposes the library's internals: the Meteorics experiment over Arch 1.2.8 depended on `Chunk.Entities`, `GetArray` and the arrays of `QueryDescription`, so it was tied to one Arch version.
- `ReadOnlySpan<T>` makes read access a compile-time rule, and `foreach` deconstruction names the spans instead of positional members such as `GetColumn1`. The type arguments are the BCL spans themselves, not wrappers such as `Read<T>` and `Write<T>`, so they pass to `TensorPrimitives` unchanged, and indexing them depends on no inlined wrapper, which Mono AOT inlines less readily than RyuJIT.
- The library cannot get `T` from a span type argument without reflection, so the generator registers every span it sees in a `Query<...>` through a module initializer. A system needs the generator anyway, and the query class needs no `partial`, unlike the `[Query]` structs this replaced.
- `IWith<...>` and `IWithout<...>` describe their filters through default members that are `internal`, so only Pixely.Ecs implements them and the library reads them without reflection.
- The contract in the constructor states what a system reads and which structural changes it makes, for reviews and for a parallel scheduler later. Typed writers make the changes as exact as the reads; Bevy's `Commands` can make any change.
- Queries and writers reach the system through its constructor, like services. A writer records only while the scheduler runs its system's `Update`, so one used in the constructor or kept for an event handler throws. Passing them to `Update` instead protected nothing more, because a system could still keep them, and every contract type was written twice, in `ISystem<...>` and in `Update`.
- Pixely's DI resolves each parameter by its exact type and has no open-generic registration, so the generator intercepts `AddSystem<T>()` and creates the queries and writers itself instead of registering every closed type. `SystemScheduler.Add` takes a function that creates the system through a `SystemBuilder`, so it needs no generated code.
- Systems have no `UpdateOrder`: their order is their registration order, so the order of their changes at a sync point is the order they run in, without a writer knowing its system.
- Sync points follow Flecs: one after the last system, and one before a system that names a component changed since the last sync point. Bevy inserts one between explicitly ordered systems, and with every system ordered by registration that would be a sync point after every system with a writer.
- Structural changes on the world throw while a query runs, so spans never point at moved rows. `Create` stays allowed, because every query requires a component and a new entity has none.

Archetypes store rows in chunks of a power of two of rows, at least 64, so a full chunk is a whole number of SIMD vectors and an archetype grows without copying or reaching the large object heap.

Tags implement `ITag` and get no column. Spotting empty structs instead needs reflection on each component type, which Native AOT may strip unless every generic component parameter is annotated, and a wrong answer would drop component data silently. `default(T) is ITag` needs no reflection, and PXECS013 rejects a tag that holds data.

Cost: an archetype with one entity still has a whole chunk of up to 16 KB. A tag must opt in with `ITag`; an empty struct without it takes one byte per row. An `EntityDestroyer` forces a sync point before every later system with a query. A system that needs `Get<T>` injects `World`, which its contract does not show. The constructor mixes the contract with services, which only their types tell apart. A query is a class that renderers share, so a system can enumerate it in its constructor or keep it past `Update`; only writers and lookups are checked. Each span of a chunk comes from a registered delegate, one call per span per chunk. A query of one span has no deconstruction in C#, and code that only counts rows names the whole `Columns<...>` type. Pixely.Fitness and the Peach architecture still expect Systems to be `IUpdatable`. It is not packaged, so games reference the projects from source.

## 2026-10-20: Color is sRGB in images and windows and linear in shaders, with no opt-in

Textures created from images default to `R8G8B8A8UnormSrgb`, every window claimed for the GPU device has an `SDR_LINEAR` swapchain, and shaders work in linear light. `Color` and `FColor` became `SrgbColor` (bytes, as authored) and `LinearColor` (floats, what the GPU works with); `SrgbColor` converts to `LinearColor` implicitly. The palette `Colors` became `SrgbColors`, and `LinearColors` replaces `FColors` with the same palette converted to linear light. This reverses the closing of #647 and supersedes #648.

- An opt-in mixes formats, and every problem found in #647 and #648 came from that: Pixely.Ui writing sRGB values that an sRGB swapchain would encode twice, a sprite cache keyed by path that cannot hold one image in two formats, offscreen windows needing their own check, and a fallback that keeps the shader encode anyway. With one pipeline none of these exist.
- There is no fallback to the SDR swapchain, because it would need every shader to encode. Window creation fails without `SDR_LINEAR`. The browser gets it from XDL_wgpu `v20261020.072236`, which renders through an sRGB view of the canvas.
- The offscreen window of headless mode picks `B8G8R8A8UnormSrgb` or `R8G8B8A8UnormSrgb` by device support before its SDL window exists. This supersedes the fixed `B8G8R8A8Unorm` of the 2026-09-26 headless windows entry and the `CreateTexture(Image)` default of the 2026-10-12 models entry.
- Vertex colors stay `SrgbColor` bytes and shaders decode them per vertex, because linear light in 8 bits bands in dark tones.
- Textures an app creates itself keep explicit formats: a compute shader cannot write sRGB, so its target is UNORM or float, and a blit to the window encodes it.

Cost: `Color`, `FColor`, `Colors` and `FColors` are gone, a breaking change across the public API. Data textures created through `ITextureLoader`, `ICopyPass` or `GpuMemorySystem` (`CreateTexture`, `CreateTextureArray`) without a format are now sRGB and need `R8G8B8A8Unorm` passed explicitly. Translucent fills and anti-aliased text blend in linear light, so the UI looks slightly different. A uniform pushed as `SrgbColor` without a cast reaches the shader as bytes, because the push methods are generic. GPU behavior has no automated tests, since CI has no GPU. `FontRasterizationMode.Lcd` and color glyphs remain wrong, as before.

## 2026-10-12: The shader toolchain is imported as a project SDK

`Pixely.SdlangCompiler.props` and `.targets` import the `SlangDxcBundle.Toolchain` project SDK with an explicit `Version` instead of referencing the package. The SDK references only the build host's `SlangDxcBundle.Toolchain.<rid>` package and exposes its directory as `SlangDxcToolchainDirectory`.

- From `2026.19.0.1` the toolchain ships as an MSBuild project SDK plus one package per build host, and a `PackageReference` to the SDK package fails the build. The earlier single package carried all five platforms.
- Importing it from the shader props covers package consumers and source builds with one pin, because the Pixely SDK imports the same file.
- The toolchain SDK owns host detection, so Pixely no longer maps the host to a Slang platform, and macOS x64 hosts gain the compiler.
- The pin moves from `SlangVersion` to `SlangDxcToolchainVersion`, because the fourth component is a packaging revision, not part of the Slang version. The shader cache compares this version, so a new packaging revision recompiles shaders.

Cost: MSBuild resolves a named SDK once per build, so another project SDK in the same build that pins a different toolchain version silently wins, with only warning MSB4240.

## 2026-10-12: 3D models are binary glTF read in managed code

`Pixely.Models` loads binary glTF 2.0 (`.glb`) at runtime with its own reader: `System.Text.Json` with a source-generated context for the JSON chunk, and bounds-checked accessor reads for the binary chunk. Images inside the file are decoded through the new `IImageDecoder`. Pixely ships no model shader.

- glTF defines skins and keyframe animation, and its binary buffers need little conversion before upload.
- A managed reader adds no native library, so the desktop, NativeAOT and the browser share one code path. Assimp or another native importer would need its own archive for the browser.
- A primitive has two vertex buffers: the existing `PositionTextureNormalVertex` in slot 0, and `SkinVertex` (joints and weights) in slot 1 only for skinned primitives. Static and skinned primitives share one type, and static ones carry no unused joint data. A pipeline declares its slots, so static and skinned primitives need different pipelines.
- `IImageDecoder` is a separate interface instead of a new member of `IImageLoader`, so apps that implement `IImageLoader` keep compiling. `SdlImageDecoder` checks the decoded size and row pitch, and `CreateTexture(Image, TextureFormat)` checks the image before upload, so a malformed image fails instead of being truncated.
- Base color textures are uploaded as `R8G8B8A8UnormSrgb`, as glTF requires. `CreateTexture(Image)` keeps `R8G8B8A8Unorm`.
- The loader checks what it uses for memory safety and correct output, and repairs faults that exporters commonly write and that leave the data usable: weights that do not sum to 1, a joint repeated in one vertex, normals and rotations that are not unit length. Rejecting them would fail models from common tools. It is not a conformance validator.
- Matrices stay `System.Numerics` row-vector matrices, consistent with the rest of Pixely; reading glTF's column-major numbers in order gives them directly.
- Allocations are bounded before they happen. Accessor conversions and the vertex, skin and index arrays built from them are cached, and together with the skins' inverse bind matrices and the scenes' mesh nodes they are limited to 16 times the file's size, because accessors can alias the same bytes and scenes can share one hierarchy. Images are limited to 16,384 pixels a side and 16,384 × 16,384 pixels together, read from the PNG or JPEG header before decoding, because a small compressed image can declare gigabytes of pixels. The image limit is fixed rather than relative to the file, because valid textures compress far better than 16 to 1.
- A `ModelInstance` owns a pose, its matrices and its joint buffers, and a registered `ModelInstanceFactory` creates it, so a game never handles `GpuMemorySystem` for animation. The game calls `Animate` from its own update instead of Pixely updating every instance: the game decides time and which animation plays, and `GpuMemorySystem` already puts every upload of a frame into one copy pass, so a central update would batch nothing more. An instance does not own its `Model`, because several instances share one.

Cost: no `.gltf` files, morph targets, sparse accessors or extensions that a file requires. A game writes its own model shaders and picks the front face per draw, because glTF is right-handed and counter-clockwise while Pixely's pipeline default is clockwise and `PerspectiveCamera` is left-handed. Images decode through SDL3_image, so the browser decodes only the formats its `SDL3_image.a` was built with. The loader keeps no cache across loads. A file whose converted data exceeds 16 times its size fails, and so does one whose images exceed 1 GiB decoded, even if it is valid glTF. A small file can still make the loader use about 3 GiB while it uploads its largest image.

## 2026-10-06: Uploads are waited for on V3DV

With `PixelyConfig.UploadWait` on, `GpuMemorySystem.Submit()` submits the uploads with `SDL_SubmitGPUCommandBufferAndAcquireFence` and waits on the fence, so a render coordinator submits the frame only after its uploads have finished. `Automatic`, the default, turns it on when the device's driver name (`SDL_PROP_GPU_DEVICE_DRIVER_NAME_STRING`) starts with `V3DV`, and off everywhere else. `PIXELY_UPLOAD_WAIT` overrides it.

- On the Raspberry Pi 5 (V3DV, Mesa 25.0.7 and 26.1.6), geometry read from a storage buffer updated every frame had holes on the 64 px tile grid: binning ran before the upload finished. SDL ends the copy with the transfer write to shader read barrier, so it is a driver bug, but Pixely still has to draw correctly there.
- Of the measured workarounds, only waiting before the frame's command buffer fixed both Mesa versions (0 of 30 damaged frames, against 6 to 30 without it). Waiting at the start of each frame left 24 of 30 on Mesa 25.0.7. Turning binning off with `V3D_DEBUG=cl_nobin` dropped from about 50 to 20 fps. The wait cost no measurable frame rate on the Pi.
- The measurement waited for the whole GPU to go idle. The fence covers the upload submission, which is what the frame needs, so it does not wait for the whole GPU to go idle.
- Elsewhere the wait only stops the uploads from overlapping with the work before them, so it is not on by default for every driver.
- Mesa reports `V3DV Mesa` as the driver name and the version separately, so the match covers every Mesa version until the bug is fixed. The variable lets a newer Mesa be checked without a rebuild.
- The browser cannot wait on a fence, and V3DV is never its driver, so `On` fails there and `Automatic` is off.

Cost: on V3DV the CPU waits for the frame's copies every frame. A V3DV driver that reports another name is not matched. Buffer cycling still needs no fences (2026-09-29 and 2026-09-26): this fence works around the driver, not the tracking of copies in use.

## 2026-10-05: Draws have no base vertex or first instance

`RenderPass` has no first instance or vertex offset, and the compiler maps `SV_InstanceID` and `SV_VertexID` to Slang's Vulkan semantics for SPIR-V and Metal. Pixely no longer requests Vulkan's `shaderDrawParameters`.

- Slang compiles `SV_InstanceID` and `SV_VertexID` as Direct3D defines them. For SPIR-V it subtracts `BaseInstance` and `BaseVertex`, which needs `shaderDrawParameters`, and Slang has no option to turn that off.
- SDL skips every Vulkan device that lacks a requested feature. The Raspberry Pi 5's V3D driver lacks this one, so SDL picked llvmpipe, Mesa's CPU renderer.
- WGSL never subtracts, so a non-zero start already gave different IDs in the browser.
- The Vulkan semantics read the plain index, so with every draw starting at 0 they equal the Direct3D ones. DXC rejects them, so the Direct3D 12 and WGSL compiles keep the original semantics.
- The slangc options are part of the source hash, so outputs compiled before this change are compiled again.
- The defines match only the exact spelling, while Slang reads semantics in any case. A SPIR-V output that still declares the `DrawParameters` capability fails the build, which also catches the base vertex, base instance and draw index semantics.

Cost: a shader that needs a start reads it from a uniform, and a draw over a later vertex range needs its own indices. The shader metadata no longer records whether a vertex shader reads either ID.

## 2026-10-05: Performance diagnostics are logged, measured at three points, and count no draws

With `PIXELY_DIAGNOSTICS`, `PixelyApp.RunFrame` and `RenderCoordinator<T>` record the update, the render and the swapchain wait of every frame in `FrameTimings`, through `IFrameTimingRecorder`. `PerformanceReport`, an updatable, logs the average, 95th percentile and maximum of each every 5 seconds, together with gen0 collections and GPU memory, and the GPU device at startup and each window's swapchain. Without diagnostics the recorder is `NullFrameTimingRecorder` and no report is created. `Build()` registers them last, and the report resolves the logger factory and the GPU device only while diagnostics are on, so diagnostics change neither when the app's services are created nor when they are disposed. This replaces `PerformanceTracker`, which Pixely never registered and which read the frame clock, so in headless mode it reported the fixed step.

- The numbers must be readable on a kiosk compositor, where the screen is the game, so they go to the log instead of an overlay.
- .NET metrics with `dotnet-counters` would give percentiles and gen0 collections for free, but a NativeAOT app needs `EventSourceSupport` at build time. Diagnostics must turn on in the shipped build with an environment variable.
- SDL GPU has no timestamp queries, so the swapchain wait stands in for GPU time, and timing each renderer would measure only command recording.
- Recording and reporting are separate, so the callers depend on a recorder that does nothing while diagnostics are off instead of checking for null, and another reader, such as an overlay, can use the same timings.
- Counting draws, binds, render passes and uploaded bytes needs hooks in `RenderPass`, `CommandBuffer` and `CopyPass`. The time split already tells a CPU-bound frame from a GPU-bound one, so they are left out.

Cost: the swapchain wait of a custom render coordinator, or of a `RenderCoordinator<T>` the app creates itself, counts as render time. Only the root provider's GPU device is reported. Reports end after 4096 frames at most, so a loop faster than about 800 frames per second reports more often than every 5 seconds. The report is written during the next frame's update, so that update also counts writing it.

## 2026-10-02: Brotli decompression in the page is opt-in

With `PixelyBrowserBrotli=true`, the default `main.js` installs a resource loader that fetches the `.br` copy of `dotnet.native.wasm`, of each assembly and of the ICU data and decompresses it in the page. The page uses `DecompressionStream` where it decodes Brotli and google/brotli's JavaScript decoder from jsDelivr elsewhere. Without the property nothing changes.

- GitHub Pages sets no custom headers. It compresses `.wasm` with gzip on the fly and serves `.br` files as opaque files, so the browser never uses the Brotli copies a publish writes.
- Brotli makes the ManyWorlds `_framework/` download 22% smaller than gzip (2.1 MB against 2.6 MB), and 30% smaller with `RunAOTCompilation` (3.6 MB against 5.1 MB).
- Chrome's `DecompressionStream` has no Brotli, so a decoder in JavaScript is the only way there. It costs 67 KB gzipped, loaded only where it is needed.
- On a host that sends `Content-Encoding: br` itself, the loader saves nothing and makes Chrome slower, so it is off by default.
- `main.js` is a static file and cannot read MSBuild properties. A publish puts the property in its boot config as `extensions.pixely`, which the runtime hands to `onConfigLoaded` before any download, with `disableIntegrityCheck` and `disableNoCacheFetch` for the fallback. A build writes no `.br` files and gets no extension, so the loader stays off there.
- Anything that keeps a `.br` file from being used falls back to the original: a missing file, a failed request, or a decoder that did not load. A streamed file that fails partway continues from the original, because the runtime does not refetch `dotnet.native.wasm`. A file from another origin is left to the runtime, because CORS hides its `Content-Encoding`.
- The decoder comes from jsDelivr, pinned to the v1.2.0 tag and its SHA-256, so neither the repository nor the package carries a 150 KB minified file. `import()` checks no integrity, so the page fetches it with one and imports it from a data URL.

Cost: streamed files skip the runtime's integrity check, and only a truncated one recovers: a corrupt one can decode to wrong bytes before it fails, since Brotli has no checksum. Files the JavaScript decoder decodes are checked against their hash. The fallback never sees a file's own cache or credentials setting. `onConfigLoaded` belongs to the loader, so a page that sets its own calls `configureCompressedResources` from it. Where the decoder is needed, each player's browser contacts jsDelivr, and a page whose Content-Security-Policy blocks it, or blocks `data:` scripts, downloads the originals. On Chrome, `dotnet.native.wasm` compiles only after it has fully arrived and been decoded, about 220 ms for a 13 MB AOT runtime. A publish whose `.br` files were removed costs one failed request per file before the original, and the browser logs each one as a console error.

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
