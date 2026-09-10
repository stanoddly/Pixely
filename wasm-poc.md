# Browser proof of concept — working checklist

Goal: the `Triangle` tutorial rendering in a browser through Pixely's own render pipeline,
on SDL_GPU's WebGPU backend (Option A of `wasm.md`). One narrow vertical slice; hacks are
allowed and get recorded in Phase 6.

Branch: `worktree-wasm-target`. Started 2026-09-10.

## Established facts

| Thing | State |
|---|---|
| .NET SDKs present | 9.0.120, 10.0.111. No .NET 11, no `wasm-tools` workload |
| .NET 11 | 11.0.100-rc.1.26425.128, go-live. `release/11.0` pins `EmsdkVersion 6.0.2` |
| Emscripten | Not installed; comes from the wasm-tools workload packs |
| SDL PR #16020 | Open, **no longer a draft**, `MERGEABLE`, updated 2026-09-08, head `TheeStickmahn:main` |
| Repo TFMs | 67 projects `net10.0`, one `netstandard2.0`; tutorials set `PublishAot=true` |
| `PixelyApp.Run()` | `while (true)` returning `int`, confirmed tab-hanging |
| Browsers | `firefox`; ungoogled-chromium flatpak; `npx` for Playwright |
| Disk | 800 GB free |

## Phase 0 — Toolchain (blocking) — DONE

- [x] Install .NET 11 RC1 side by side into `~/.dotnet` (11.0.100-rc.1.26425.128)
- [x] ~~`global.json` pinning it~~ — dropped. A pinned `global.json` would break the system
      `dotnet` (10.0.111) for the other 67 projects in this worktree. Invoke
      `/home/stanoddly/.dotnet/dotnet` by absolute path instead.
- [x] Install the `wasm-tools` workload — pulled `Microsoft.NET.Runtime.Emscripten.6.0.2.*`
      packs, confirming `wasm.md`'s Emscripten 6.0.2 claim on the shipped RC
- [x] Locate the Emscripten 6.0.2 SDK inside the workload packs
- [x] **Gate passed:** `emcc` compiles and runs a hello-world under node

Environment script: `<scratchpad>/emenv.sh`. Two things that were not obvious:

- The packs' `.emscripten` reads `FROZEN_CACHE` as `bool(os.getenv('FROZEN_CACHE', 'True'))`,
  so any non-empty value freezes the cache, `"0"` included. It must be set to the empty
  string, and `EM_CACHE` repointed at a writable copy of the pack cache, or ports cannot build.
- The worktree guard in this session refuses `source` inside compound commands, so build steps
  are written as self-contained scripts and run as `bash /abs/path/script.sh`.

## Phase 1 — Native proof, no .NET — DONE, PASS

SDL_GPU's WebGPU backend builds with the .NET packs' Emscripten and renders correctly in a
real browser. **No patches to SDL's source were needed** and the configure and build were clean
on the first attempt, which was the single biggest schedule risk in `wasm.md`.

- [x] Shallow clone: `TheeStickmahn/SDL_wgpu`, branch `main`, commit
      `e56d6ca5b972287b408fd6ed40dd514bed60d9ab`, exactly the PR #16020 head
- [x] Built with `emcmake cmake` + ninja
- [x] Linked `examples/gpu/01-orbital-simulation`
- [x] **Gate passed:** the asteroid ring renders, evolves between frames, and the console shows
      zero WebGPU errors and zero validation messages

Configure line:

```
emcmake cmake -S <scratch>/SDL_wgpu -B <scratch>/build-wasm -G Ninja \
  -DCMAKE_BUILD_TYPE=Release -DSDL_SHARED=OFF -DSDL_STATIC=ON \
  -DSDL_WEBGPU=ON -DSDL_WEBGPU_EMSCRIPTEN=ON \
  -DSDL_EXAMPLES=ON -DSDL_TESTS=OFF -DSDL_INSTALL_TESTS=OFF
```

Link flags for the example: `-sALLOW_MEMORY_GROWTH=1 -sASYNCIFY=1 --use-port=emdawnwebgpu`.

Facts that change later phases:

- The Emscripten in the packs is **6.0.3**, not the 6.0.2 the pack name advertises.
- `SDL_WEBGPU_EMSCRIPTEN=ON` is what injects `--use-port=emdawnwebgpu`
  (`cmake/sdlchecks.cmake:958`). The PR text calls this option `SDL_WEBGPU_USE_PORTS`, but at
  this commit it is `SDL_WEBGPU_EMSCRIPTEN` (`CMakeLists.txt:392`). Both default to ON.
- `<scratch>/build-wasm/libSDL3.a` is a real 2.2 MB archive of 218 members including
  `SDL_gpu_webgpu.c.o`, so it suits `NativeFileReference`. It does **not** contain
  emdawnwebgpu, which arrives via `--use-port=emdawnwebgpu` at link time and is fetched from
  the network. SDL enables C++ for the WebGPU path, so the final link needs the C++ runtime.
- The example uses `SDL_GPU_SHADERFORMAT_WGSL` directly, so this gate involved no shader
  cross-compilation and does not validate Phase 3.

**The async bridge is Asyncify, and this is now the main risk.** `SDL_CreateGPUDevice`
synchronously spins on the WebGPU future (`SDL_gpu_webgpu.c:1660-1663` and `:1747`):

```c
while (!waitInfo.completed) {
    wgpuInstanceWaitAny(renderer->instance, 1, &waitInfo, 0);
    SDL_DelayNS(100);
}
```

On Emscripten `SDL_DelayNS` becomes `emscripten_sleep` when asyncify is present
(`src/timer/unix/SDL_systimer.c:141-147`), so the call unwinds and rewinds the stack. There is
no `Module.preinitializedWebGPUDevice` path in this backend, contrary to what `wasm.md`
assumed from the Evergine bindings. A caller can stay synchronous in source, but `-sASYNCIFY=1`
becomes mandatory for the whole link, .NET runtime included. **Whether an Asyncify unwind can
cross a Mono interpreter frame is the next thing to test, and it is the new blocker #1.**

## Phase 2 — .NET wasm calls SDL3 (`wasm.md` blocker #3)

**PASS. Blocker #3 is answered: .NET wasm can drive SDL3's WebGPU backend.** From C# in a
browser tab, against the statically linked fork, an SDL GPU device is created, reports the
`webgpu` driver, and accepts command buffers issued from managed code.

- [x] Minimal `net11.0` `browser-wasm` app, `WasmBuildNative=true`
- [x] Mechanism proven first with a trivial C library, so an SDL failure could not be
      mistaken for a P/Invoke failure
- [x] `NativeFileReference` to SDL3's real static archive, `ScanForPInvokes="true"`
- [x] emdawnwebgpu link args wired through
- [x] `SDL_Init`, `SDL_GetCurrentVideoDriver` and `SDL_CreateWindow` all work from C#
- [x] **Gate passed:** `SDL_CreateGPUDevice` returns and the device is usable from C#

Probe: `<scratchpad>/sdl-probe`. Rebuild and run with
`bash <scratchpad>/build-sdl-probe.sh && bash <scratchpad>/serve-and-run.sh` (port 8410).

Four build-level traps, all now solved:

1. **The P/Invoke module name is the archive's file name.** `libSDL3.a` registers as module
   `libSDL3`, so `DllImport("SDL3")` throws and the table shows `{"libSDL3", ..., 0}` with zero
   imports. Copying the archive to `SDL3.a` fixes it, and matches what `ppy.SDL3-CS` needs.
2. **`ScanForPInvokes="true"` is required** on the `NativeFileReference`, not just the reference.
3. **The SDK's Emscripten cache is frozen and read-only**, so `--use-port=emdawnwebgpu` fails
   with "Attempt to lock the cache but FROZEN_CACHE is set". Setting `WasmCachePath` to any
   path other than the pack's own flips `EM_FROZEN_CACHE` to 0
   (`BrowserWasmApp.targets:246-248`).
4. **Path spelling matters.** The SDK spells the pack path `/var/home/...`; using the
   `/home/...` symlink instead makes emscripten's sanity check see a changed config and wipe
   the cache mid-build, deleting the port it had just fetched.

### The Asyncify blocker, and how it was solved: JSPI

`SDL_CreateGPUDevice` busy-waits on the WebGPU future and relies on `emscripten_sleep` to
yield, so the wasm stack must suspend there. Asyncify cannot do it:

| Link | Result |
|---|---|
| no async support | Runtime starts, SDL works, `SDL_CreateGPUDevice` **hangs the tab forever** |
| `-sASYNCIFY=1` | **The .NET runtime never starts** |
| `-sJSPI` | **Works** |

Under `ASYNCIFY=1`, `libasync.js:152-183` (called from `preamble.js:784`) wraps **every** wasm
export in a JS wrapper, which destroys Mono's `cwrap` arities and its `WebAssembly.Table.set`
writes. It dies during `mono_wasm_load_runtime`, before any user code. Under `-sJSPI` the same
function wraps **only** exports named in `JSPI_EXPORTS` (`libasync.js:161-174`), so every Mono
export keeps its original `WebAssembly.Function`. JSPI also runs no binaryen pass, so wasm
exception handling and SIMD stay on.

**The .NET 7 era Asyncify recipe does not transfer.** Tested in full, with
`WasmEnableExceptionHandling=false`, the flags in `EmccFlags` so they apply to compiling as
well as linking, `-sASYNCIFY_STACK_SIZE=10000000`, `-sVERBOSE=1` and `-g3`: it fails
byte-identically. Wasm EH became the .NET default in .NET 8 and Emscripten moved from 3.1.x to
6.0.3; neither half of that gap can be bridged by flags.

`wasm.md` assumed an `emscripten_webgpu_get_device` escape hatch, where a device created before
startup keeps the caller synchronous. **That hatch does not exist in this backend.** What
replaces it is close in spirit though: JavaScript calls `SDL_CreateGPUDevice` itself, and
managed code only adopts the finished device.

A JSPI suspension unwinds to the nearest `WebAssembly.promising` frame, and Mono's entry export
is not promising, so nothing that starts in managed code can suspend. Called straight from
JavaScript, `SDL_CreateGPUDevice` *is* that frame, with no Mono frame beneath it. Exporting it
and naming it in `JSPI_EXPORTS` is the whole mechanism; no C of our own is involved.

There are exactly four blocking sites in `SDL_gpu_webgpu.c`:
`WEBGPU_INTERNAL_RequestAdapter` (:1662) and `WEBGPU_INTERNAL_RequestDevice` (:1762), both
inside `SDL_CreateGPUDevice`; `WEBGPU_INTERNAL_WaitForFences` (:1971), which Pixely does not
use; and `WEBGPU_WaitAndAcquireSwapchainTexture` (:4720), avoided by the non-blocking acquire in
`Window.cs`. So `SDL_Init`, `SDL_CreateWindow` and `SDL_ClaimWindowForGPUDevice` stay in managed
code, in the `BeginBoot` and `Start` exports.

```xml
<EmccExtraLDFlags>-sJSPI -sJSPI_EXPORTS=SDL_CreateGPUDevice -sALLOW_MEMORY_GROWTH=1 --use-port=emdawnwebgpu</EmccExtraLDFlags>
<EmccExportedFunction Include="_SDL_CreateGPUDevice" />
```

`EmccExportedFunction` also forces the archive member into the link, since no managed code
calls `SDL_CreateGPUDevice` any more.

```js
const bootError = program.BeginBoot();
// 64 is SDL_GPU_SHADERFORMAT_WGSL, 1 is debug mode, 0 is a NULL driver name; all plain
// numbers, so no string marshalling is needed
const device = await runtime.Module.wasmExports.SDL_CreateGPUDevice(64, 1, 0);
const startError = program.Start(device);
```

`getAssemblyExports` has to run before the boot call now, because `BeginBoot` is a managed
export.

Further traps, all load-bearing:

1. **JSPI requires wasm exception handling.** With `WasmEnableExceptionHandling=false`,
   `link.py:1739-1742` puts `invoke_*` into `ASYNCIFY_IMPORTS`, which under JSPI means
   `JSPI_IMPORTS`, so every JS-EH trampoline becomes a suspending import and startup dies in
   `mono_download_assets` with `SuspendError: trying to suspend without
   WebAssembly.promising`. So the Asyncify and JSPI configurations want *opposite* EH settings.
2. **A P/Invoke returning managed `bool` aborts Mono** at `method-builder-ilgen.c:631`, with or
   without `[return: MarshalAs(UnmanagedType.U1)]`. Returning `byte` works. `ppy.SDL3-CS` is
   unaffected: `SDLBool` is a struct wrapping one `byte`, and its string-taking overloads such
   as `SDL_CreateWindow(Utf8String, ...)` are managed wrappers over a `byte*` P/Invoke rather
   than P/Invokes themselves.
3. **`EmccExportedFunction` is the export knob.** The SDK sets `EXPORTED_FUNCTIONS` explicitly
   (`BrowserWasmApp.targets:376`), so `EMSCRIPTEN_KEEPALIVE` alone does not put a symbol on
   `Module`. `Module.wasmExports.<name>` is the post-instrumentation, promising wrapper.
4. `JSPI_EXPORTS` takes the bare wasm name (`SDL_CreateGPUDevice`); `EXPORTED_FUNCTIONS` takes
   the underscore-prefixed C name (`_SDL_CreateGPUDevice`).
5. emcc warns `-sJSPI (ASYNCIFY=2) is still experimental`. Harmless, but it needs suppressing
   in a warnings-as-errors build.

### Consequence for Pixely's architecture

Device creation cannot happen inside a synchronous managed constructor. It has to be initiated
from JavaScript, and `PixelyFactory` must accept an already-created device rather than calling
`SDL_CreateGPUDevice` itself. That is a real change to
`AddSingleton<GpuDevice, PixelyFactory>()`, and it belongs in the Phase 6 write-up as a
correction to `wasm.md`'s claim that the synchronous DI graph survives untouched.

## Phase 3 — Slang to WGSL

**DONE, PASS.** `wgsl` is now the fourth target alongside spv/dxil/metal, and
`shader.{vertex,fragment}.wgsl` are emitted for the Triangle tutorial with correct metadata.
`dotnet build` clean, `dotnet test Pixely.slnx` 1216 passed / 0 failed.

**`wasm.md` §3 was wrong and was not implemented as written.**

- [x] Establish what SDL's WebGPU backend wants
- [x] Produce valid WGSL for the Triangle and `ui_quad` shaders
- [x] WGSL arm in `SdlangCompiler`
- [x] WGSL binding-layout validation pass
- [x] **Gate passed**

The global `VulkanBindingShifts` array became a per-target
`record struct VulkanBindingShifts(ConstantBuffer, ShaderResource, Sampler, UnorderedAccess)`
with `BindingShiftsByTarget`: `(0,0,0,0)` for SpirV/Dxil/Msl, `(0,0,1,0)` for WGSL. The
validation pass reads the sampler shift from the same value that builds the command line
rather than restating it.

`ValidateWebGpuBindings` computes, per non-uniform binding, the binding Slang will emit
(`Index + ShiftFor(Type)`) against the one SDL requires (`2i` for sampled texture *i*, `2i+1`
for its sampler, `sampledTextureCount + Index` for read-only storage) and throws at build time
on any mismatch. That is what stops `-fvk-s-shift 1` becoming a silent runtime trap for the
next shader someone writes.

Three findings that correct the earlier investigation:

- **Keeping Slang warning 39001 fatal for WGSL is a no-op.** Slang emits *no* diagnostic for a
  gapped or colliding WGSL layout; it silently writes duplicate `@binding` values and exits 0.
  The new validation pass is the only guard there is.
- **`ResourceType.StorageTexture` is unreachable** in `ParseReflectionData`, which classifies
  every non-read-write, non-structured-buffer resource as `SampledTexture`. The practical rule
  today is "at most one sampled texture per stage, and none beside a storage buffer".
- **`ComputeShaderLoader` had the same missing NUL termination** as `ShaderLoader`, since
  `WEBGPU_INTERNAL_GenerateBindGroupLayoutsForComputeShader` also `strlen`s the code pointer
  (`SDL_gpu_webgpu.c:5070`).

`GraphicsShaderProgramMetadataLoader.ConvertShaderFormat` was an extra touch point not in the
original list; without it the loader throws on WGSL metadata.

What SDL's WebGPU backend actually requires, from the fork's source:

- `SDL_GPU_SHADERFORMAT_WGSL` = `1u << 6` = 64 (`include/SDL3/SDL_gpu.h:1049`); the device
  advertises it at `SDL_gpu_webgpu.c:5964`.
- Shaders are **WGSL source text**, and entry point names pass through verbatim, so Slang's
  `vertexMain` / `fragmentMain` need no mapping.
- A new device-creation property `SDL_PROP_GPU_DEVICE_CREATE_SHADERS_WGSL_BOOLEAN`
  (`"SDL.gpu.device.create.shaders.wgsl"`, `SDL_gpu.h:2416`) has to be set.
- The backend infers the bind group layout by **string-scanning the WGSL**
  (`SDL_gpu_webgpu.c:2138`) and requires bindings within a group to be **dense, sequential and
  interleaved**: texture, then its sampler, then the next pair, with storage resources after
  (`SDL_gpu.h:3088-3128`, enforced at `SDL_gpu_webgpu.c:2819-2839`). Gaps make pipeline
  creation fail at runtime, not at compile time.
- Shader bytes must be **NUL-terminated**: the backend calls `SDL_strlen` on the code pointer
  (`SDL_gpu_webgpu.c:3459`). `ShaderLoader.cs:110-118` passes raw file bytes today.

Why §3 is wrong: its `-fvk-t-shift 10 / -fvk-s-shift 20` scheme produces exactly the gapped,
non-interleaved layout SDL forbids. `-fvk-s-shift 1` is correct for the repo's shaders today,
because every one of them has at most one texture+sampler pair per space, but per-class shifts
are additive and cannot express SDL's texture-at-2i, sampler-at-2i+1 rule in general. The
durable fix is for the compiler to compute WGSL bindings from the reflection JSON it already
parses and inject `[[vk::binding(n, set)]]` into a WGSL-only copy of the source. That cannot go
in the shared shader source, because SPIR-V wants texture and sampler to *share* a binding.

Two further `wasm.md` staleness corrections: the Triangle shader does **not** collide (it has
no texture or sampler at all), and the `-warnings-disable 39001,39013,39029` calls that §3
builds its argument on no longer exist. They were removed in `6739a8a` and replaced by explicit
zero shifts at `SdlangCompiler.cs:157-170`.

## Phase 4 and 5 — Pixely in the browser — DONE, PASS

**The proof of concept works.** `tutorials/Pixely.Tutorials.TriangleBrowser` renders in
Chromium through Pixely's own `IRenderer<BasicRenderContext>` pipeline on SDL_GPU's WebGPU
backend, driven by `requestAnimationFrame`.

Screenshot: `<scratchpad>/pixely-triangle-browser.png`

```
boot: SDL_CreateGPUDevice returned 11586192
gpu driver as Pixely sees it: webgpu
frames rendered: 1
frames rendered: 60
frames rendered: 300
```

No page errors, no WebGPU validation messages, tab responsive throughout.

**The Triangle tutorial does not draw a triangle.** `TriangleRenderer.Create` binds
`PositionShapes.VerticalQuad` as a `TriangleStrip` and `Render` pushes `FColors.Magenta`, so it
fills clip space with magenta. Verified in the source, not just inferred from the picture. The
draw is genuinely Pixely's: the clear colour is grey, a control run with `NullRenderer` leaves
the canvas black, and reaching magenta requires the Phase 3 WGSL to compile, the pipeline to
build, the vertex buffer to upload and the fragment push constant at `@group(3) @binding(0)`
to arrive.

Desktop is unharmed: `dotnet build` clean at 0 warnings, `dotnet test Pixely.slnx` 1217 passed
and 0 failed, against a 1216 baseline plus one new `webgpu` case in `GpuBackendSelectionTests`.

### Changes to Pixely proper

- **`PixelyApp.RunFrame()`**, public and on `IPixelyApp`, runs one frame and reports whether
  another should follow. `Run()` is now `while (RunFrame()) { }`. Non-breaking for desktop, and
  it is what the browser host needs. Frame services resolve on first call rather than in the
  constructor.
- **Device adoption.** `PixelyConfig` gained `AdoptedSdlHandles(IntPtr GpuDevice, IntPtr Window)`.
  When set, `PixelyFactory` wraps the existing handles instead of creating a device and window,
  and skips `SDL_Init`/`SDL_Quit` because the browser host owns that lifetime. One nullable
  property gating three sites.
- **`Window.TryWaitAndAcquireSwapchainTexture`** uses the non-blocking
  `SDL_AcquireGPUSwapchainTexture` under `OperatingSystem.IsBrowser()`. The waiting form spins
  on `SDL_DelayNS`, which cannot suspend beneath a managed frame.

The adoption seam is scaffolding, not a design. Raw handles do not belong in a configuration
record. `PixelyAppBuilder.Build()` already uses `if (!IsRegistered<T>())` for `PixelyConfig`
and `IImageLoader`; extending that to `GpuDevice` and giving `AddWindow` the same guard would
let a host register its own device and window and leave `PixelyFactory` alone entirely. That
needs public `GpuDevice.Adopt(...)` and `Window.Adopt(...)`, since both constructors are
internal. It would also cover embedding Pixely in a host application's window, which is worth
having regardless of the browser.

### SDL itself had to be patched

**The shipped `<scratchpad>/SDL3.a` is not stock PR #16020.** Patch:
`<scratchpad>/sdl-webgpu-pixely.patch`. Two real bugs in the PR, both of which only appear once
something renders continuously, which is why its own examples did not catch them:

- `WEBGPU_AcquireSwapchainTexture` never reaped submitted command buffers. Nothing else
  decrements `submittedCommandBufferCount`, so the non-blocking acquire returned NULL forever
  from frame two onward.
- `WEBGPU_Cancel`'s entire body was `SDL_assert_release(!"...I'm lazy.")`. It now returns true
  and leaks.
- `WEBGPU_INTERNAL_ParseBindGroupLayoutEntriesFromShader` scanned for `@group`, `@binding` and
  `var` in that fixed order. WGSL does not fix attribute order and Slang writes
  `@binding(n) @group(m) var`, so the scan began at the texture's `@group`, took the *sampler's*
  `@binding`, and produced one merged entry: the inferred bind group layout lost the texture
  entirely and every textured shader failed pipeline creation. Added in Phase 7.

Error callbacks were also made to print reason and message, without which the device-loss
failure below was invisible.

### Hacks and scaffolding, to be honest about

- ~~**SDL_ttf is stubbed.**~~ Replaced in Phase 7 by a real build; see below. The finding that
  prompted it still stands: **DI singletons are eager, not lazy** — `BuildServiceProvider`
  resolves every singleton during `Build()`, so `FontSystem.Create` really does run and really
  does call `TTF_Init` even for a tutorial that draws no text.
- The browser project overrides `SdlangCompilerAssembly` between the props and targets imports,
  because the props file spells that path with the consuming project's `$(TargetFramework)`.
- `PixelyWasmScratch` in the csproj points at this session's scratchpad. Nothing builds without
  the scripts there. **This is the main obstacle to reproducing the result on another machine.**

### Known failure: SwiftShader

On Chromium's SwiftShader adapter the WebGPU device is lost right after the first presented
frame (`reason=2 Device was destroyed`), after which SDL's recreate path busy-waits inside a
WebGPU callback and throws `SuspendError`. It is the adapter, not headless mode: the same
binary fails with `--use-webgpu-adapter=swiftshader` and runs 300+ frames on Vulkan. Ruled out
with probes: Pixely disposing the device, and over-release inside SDL.

This matters for CI, where a software adapter is usually all that is available.

## Phase 6 — Write up

- [x] This document
- [ ] Fold corrections back into `wasm.md`

### What `wasm.md` got wrong

1. **§3's binding scheme** would produce exactly the layout SDL rejects. Worse, Slang emits no
   diagnostic for a bad WGSL layout, and SDL only fails at pipeline creation, so it would have
   failed at runtime in the browser. §3 also argues from `-warnings-disable` calls deleted in
   `6739a8a`, and claims the Triangle shader collides when it has no texture or sampler at all.
2. **The async escape hatch does not exist.** `wasm.md` assumed an
   `emscripten_webgpu_get_device` style preinitialized device would keep the synchronous DI
   graph intact. This backend uses an Asyncify busy-wait instead, so device creation must
   happen before managed code runs and `PixelyFactory` must adopt the result.
3. **Asyncify is not usable with modern .NET.** Not mentioned at all, and it is the single
   hardest problem in the whole exercise. JSPI solves it.
4. **The estimates look high.** `wasm.md` put Option A at 20-32 weeks with a WGSL arm at 3-6
   weeks and the main loop at 2-3 weeks. The WGSL arm plus validation, the main-loop split,
   device adoption and a rendering browser tutorial all landed in one session. That is a
   proof of concept, not a supported feature — no audio, no text, no images, no input, one
   tutorial, a stubbed SDL_ttf and a patched SDL — but the shape of the work is now known
   rather than estimated, and the unknowns that justified the range are mostly resolved.
5. **Blocker #1 was right for the wrong reason.** SDL's browser backend being unmerged is
   indeed the top risk, but not because it does not work. It works. The risk is that it has
   bugs like the two above, and no merge date.

## Phase 7 — Real SDL_ttf and SDL_image — DONE, PASS

Both libraries are built from source for wasm against the fork, the stub is gone, and
`tutorials/Pixely.Tutorials.ImageTextBrowser` decodes a PNG through `IImageLoader` and rasterises
text through `IFontSystem`, then draws both on the canvas.

Screenshot: `<scratchpad>/pixely-image-text-browser.png`

```
IMAGE size=443x410 format=Abgr8888 bytes=726520 opaquePixels=49747 firstOpaque=[310,23]=197,153,116,255
FONT size=32 measured="Pixely in the browser"->168x14 glyph"M"->8x14 rasterised=168x14 texture=168x14
```

Sources: SDL_ttf and SDL_image `main`, both reporting 3.5.0, matching the fork's SDL 3.5.0. Build
scripts are `<scratchpad>/build-ttf.sh` and `<scratchpad>/build-image.sh`.

**`IImageLoader.Load` and the font path stayed synchronous.** `wasm.md` predicted both would have
to become async in the browser. They did not, and nothing in the probe awaits. Content arrives from
`EmbeddedContentSource`, so the bytes are already in memory when `Load` is called; decoding and
rasterisation are pure CPU work in the wasm module with no host round trip. The prediction only
holds for a content source that fetches over the network.

Three findings:

1. **The Emscripten freetype port cannot be used with .NET.** `tools/ports/freetype.py:21-25`
   returns the `-legacysjlj` variant for any `SUPPORT_LONGJMP=wasm` link and ignores
   `WASM_LEGACY_EXCEPTIONS`. The .NET link is `-fwasm-exceptions -s WASM_LEGACY_EXCEPTIONS=0`, so
   that variant's legacy instructions make the browser reject the module with "module uses a mix of
   legacy and new exception handling instructions". Reproduced in four lines with plain `emcc`.
   The fix is to vendor freetype and harfbuzz into `SDL3_ttf.a`, which SDL_ttf's own CMake does for
   a static build, so the consuming link needs no port at all.
2. **SDL_image needs no external decoder.** Turning off AVIF, JXL, TIFF, WebP and libpng leaves
   PNG, JPEG, BMP, GIF, QOI, SVG, TGA, PNM, PCX, LBM, XCF, XPM, XV and ANI on the vendored
   stb_image and SDL_image's own loaders, so `SDL3_image.a` is self-contained.
3. **SDL's WGSL parser was order-dependent**, which no Phase 3 shader could expose because the
   Triangle has neither texture nor sampler. See the patch note above.
