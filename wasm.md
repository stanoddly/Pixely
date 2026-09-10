# Targeting browsers with Pixely — research

Status: research only, nothing implemented. Written 2026-09-09 against branch `ui-update-phase`.
Reviewed by codex against the codebase; findings folded in and independently re-verified.

## Verdict

**The picture changed in Pixely's favour while this was being written.** There is now an active,
substantially complete WebGPU backend *for SDL_GPU itself*:

[libsdl-org/SDL PR #16020](https://github.com/libsdl-org/SDL/pull/16020) — "GPU: Experimental
WebGPU SDLGPU Backend", open draft, 114 commits, **34 of 34 SDL_GPU examples passing** on
Windows, Linux **and web**, linking emdawnwebgpu automatically for Emscripten builds, with a
live browser demo. SDL maintainers (slouken, icculus, thatcosmonaut, TheSpydog) are actively
reviewing; as of 2026-09-08 they were posting screenshots of the FEZ engine rendering through
it. The author is on a health hiatus and maintainers have said "no rush", so it is unmerged
and undated — but it is no longer vapour.

That reframes everything. The two options are:

- **Option A — wait for SDL #16020 and port on top of SDL_GPU.** Pixely keeps its
  architecture, its enums, its `Gpu/` layer, its `Window.cs`, its input stack. The browser
  work shrinks to: a WGSL arm in the shader build, a non-blocking main loop, and browser
  answers for image loading, audio and content. **Weeks to a few months, not years** — but
  gated on someone else's unmerged draft PR.
- **Option B — build Pixely's own GPU backend seam and a WebGPU backend behind it.**
  Independent of SDL's timeline, and the seam is defensible on its own merits, but it is
  **~8–14 engineer-months** for public-API parity and it duplicates work SDL is already doing.

**Recommendation: track Option A, and do the cheap work that both options need anyway** — the
WGSL shader arm (which needs a target-specific binding convention, see §3) and the main-loop
contract. Do not start Option B's backend seam on the strength of the browser target alone.

The rest of this document is the evidence, including everything that is hard *regardless* of
which option wins.

### Toolchain: target .NET 11, not .NET 10

| | .NET 10 | .NET 11 (preview 7) |
|---|---|---|
| Emscripten | 3.1.56 | **6.0.2** |
| WebGPU binding | `-sUSE_WEBGPU` only | `--use-port=emdawnwebgpu` only (`-sUSE_WEBGPU` **removed** in 4.0.18) |
| .NET WebGPU binding available | Yes — `Evergine.Bindings.WebGPU` | No — would need generating (Option B only) |
| SDL3 port | No (arrived 4.0.15) | Yes — SDL3 **3.4.2** + `sdl3_ttf` |

SDL #16020 links emdawnwebgpu, which needs Emscripten 4.0.10+. **That alone makes .NET 11 the
target for Option A**, and .NET 11 is also where the SDL3 and sdl3_ttf ports live.

---

## 1. Where Pixely is today

### SDL coupling

| Area | SDL surface |
|---|---|
| `src/Pixely/Gpu/` (5 300 lines) | SDL_GPU device, command buffer, render/compute/copy passes, pipelines, textures, buffers, samplers |
| `src/Pixely/Window.cs` (697 lines) | `SDL_Window`, swapchain acquire, hit test, window shape, icon, fullscreen, file dialogs |
| `src/Pixely/Input/` (1 844 lines) | `SDL_Event` pump, keyboard, mouse, gamepad, text input, clipboard |
| `src/Pixely/Text/` (762 lines) | SDL_ttf: `TTF_OpenFontIO`, `TTF_RenderText_*_Wrapped`, hinting flags |
| `src/Pixely/Content/SdlImageLoader.cs` | SDL_image |
| `src/Pixely.Audio/` (1 090 lines) | SDL_mixer |
| `src/Pixely/PixelyFactory.cs` (378 lines) | **The central coupling point** — `SDL_Init`, hints, app metadata, device creation, window creation, input service construction, shutdown |
| `src/Pixely/FrameContext.cs`, `EventService.cs` | `SDL_GetTicksNS`, `SDL_PollEvent` |

For **Option A this table is irrelevant** — all of it keeps working. For Option B it is the
work item, and `PixelyFactory` plus the SDL pointers stored directly inside every GPU resource
object mean a backend extraction is more than wrapping the calls in `Gpu/`.

### Public enums are SDL constants

```csharp
// src/Pixely/Gpu/TextureFormat.cs
public enum TextureFormat : ushort
{
    R8G8B8A8Unorm = SDL.SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_R8G8B8A8_UNORM,
    ...
}
// src/Pixely/Gpu/GpuDevice.cs — cast straight across
SDL3.SDL_GPUTextureSupportsFormat(SdlGpuDevice, (SDL_GPUTextureFormat)format, ...)
```

The full list is wider than the GPU layer: `TextureFormat`, `PixelFormat`, `BlendFactor`,
`BlendOp`, `ColorComponentFlags`, `TextureUsage`, `TextureType`, `DepthBufferFormat`,
`ShaderFormat`, `PrimitiveType`, `SampleCount`, `CompareOperation`, `StencilOperation`,
`VertexElementFormat`, `FillMode`, `CullMode`, `FrontFace`, sampler filter/address modes, and
on the input side `VirtualKey`, `Scancode`, `GamepadButton`. SDL types also leak through
public members: `Mouse.MouseId`, `PlatformInfo.SdlVideoDriver`, colour conversions, a public
stencil-state conversion.

Worth being precise: **decoupling these need not be a breaking change.** Every existing
numeric value can be preserved verbatim while the SDL references are removed from the
declarations and translation moves inside each backend. Only a redesign that renumbers would
break consumers, and nothing forces that. (Adding a `Wgsl` member to `ShaderFormat` likewise
just needs a free bit, not a renumbering.)

### What genuinely helps

- **No managed background execution.** No `Thread`, `Task.Run`, `ThreadPool`, `Parallel`, no
  `async` anywhere in `src/`. There *are* synchronisation primitives — `ManualResetEventSlim`
  and locks in `Window.cs`, locks in GPU memory tracking, `ConcurrentDictionary`/`Interlocked`
  in `Pixely.Core` (17 sites). So the accurate claim is "no background execution", not "no
  threads anywhere". Either way, single-threaded wasm is not fighting the design.
- **DI activation is source-generated, not reflective.** Registration and constructor
  activation are emitted by a Roslyn generator. The runtime still uses `System.Type`,
  trimming annotations and `Type.IsAssignableFrom` in `ServiceDescriptor.cs`, so "no
  reflection-based activation" is true and "no reflection" is not. Good for wasm AOT; **not
  yet proven** — there is no browser publish or trim test in the repo.
- **`IRenderer<T>` / `IRenderCoordinator` / `IRenderContextProvider`** insulate user rendering
  code from frame orchestration. They do **not** insulate the application entry point.
- **Seams exist but are shallow.** `IImageLoader`, `ContentSource`, `IKeyboardService` /
  `IMouseService` / `IGamepadService` / `ITextInputService` / `IClipboardService`,
  `IFontSystem` / `IFont`, and `IAudioSystem` / `IAudioClip` are all real interfaces. But
  `PixelyAppBuilder.Build()` registers the SDL implementations unconditionally, and the audio
  and font interfaces hand back concrete SDL-backed types. They reduce work; they are not
  drop-in extension points yet.
- **Slang emits WGSL.** Confirmed against the bundled `SlangDxcBundle.Toolchain 2026.17.0` —
  `slangc -h target` lists `wgsl`, `wgsl-spirv`, `wgsl-spirv-asm`. The default binding
  assignment needs per-target shift flags to be valid — see §3.

---

## 2. Toolchain facts

### The Emscripten version is the pivot

| Branch | `EmsdkVersion` | Source |
|---|---|---|
| `dotnet/runtime` `release/10.0` | **3.1.56** | `eng/Versions.props`, product version 10.0.13 |
| `dotnet/runtime` `release/11.0-preview7` | **6.0.2** | `eng/Versions.props` |
| `dotnet/runtime` `main` | 6.0.2 | `eng/Versions.props` |

From Emscripten's own `ChangeLog.md`:

- **4.0.10 (2025-06-07)** — `-sUSE_WEBGPU` deprecated in favour of the external emdawnwebgpu
  port, "a fork of Emscripten's original bindings, implementing a newer, more stable version
  of the standardized `webgpu.h` interface".
- **4.0.15 (2025-09-17)** — SDL3 port updated to 3.2.22.
- **4.0.18 (2025-10-24)** — **`-sUSE_WEBGPU` was removed.** Verified directly: `USE_WEBGPU`
  appears once in `src/settings.js` at tag 3.1.56 and zero times at tag 6.0.2.
- **5.0.0 (2026-01-24)** — SDL3 port → 3.2.30.
- **5.0.3 (2026-03-14)** — `sdl3_ttf` port added.
- **5.0.5 (2026-04-03)** — SDL3 port → **3.4.2**.

Note the emdawnwebgpu port is *remote/external*; its instructions do allow supplying a **local
port** to an older Emscripten with a compatibility warning. So a .NET 10 + emdawnwebgpu
integration is not strictly impossible — just custom, unsupported and pointless once .NET 11
ships.

### What the Emscripten SDL3 port gives you

`tools/ports` at tag 6.0.2 has `sdl3.py` + `sdl3/` and `sdl3_ttf.py`. There is **no
`sdl3_image` and no `sdl3_mixer` port** (only SDL2 equivalents), so `SdlImageLoader` and
`Pixely.Audio` need browser answers under either option.

The port is SDL **3.4.2**, whose `src/gpu` is `d3d12`/`metal`/`vulkan` — i.e. today's port
gives video, events, input and TTF but **not** SDL_GPU. PR #16020 is precisely what closes
that gap.

### `ppy.SDL3-CS` has no browser RID

Verified in the local package cache (`2026.722.0`): win-x64/x86/arm64, osx-x64/arm64,
linux-x64/x86/arm64/arm, ios, android-*. No `browser-wasm`. Same for `SDL3_image`,
`SDL3_ttf`, `SDL3_mixer`.

This is a packaging gap rather than a capability gap on .NET 11 — the native side can come
from `--use-port=sdl3`. The open question is whether the managed `DllImport("SDL3")`
declarations resolve against a statically linked Emscripten port: .NET wasm needs P/Invoke
tables generated at build time (`WasmBuildNative`, `NativeFileReference` with
`ScanForPInvokes`, or an explicit module mapping). **Unverified, and it gates Option A
entirely** — if `SDL3-CS` cannot bind to the port, Option A needs a fork of the bindings or a
shim before it can go anywhere.

### `Evergine.Bindings.WebGPU` — relevant only to Option B, and .NET 10 only

Inspected `2026.3.31.26`: `lib/net10.0/`, `runtimes/browser-wasm/wgpu_native.c`, and a
`.targets` that sets `-s USE_WEBGPU` and adds a `NativeFileReference` with
`ScanForPInvokes="true"`. Full legacy `webgpu.h` surface (184 `wgpu*` entry points), including
`emscripten_webgpu_get_device` and `WGPUSurfaceDescriptorFromCanvasHTMLSelector`.

`emscripten_webgpu_get_device()` returns a device JS created before the runtime starts
(`Module.preinitializedWebGPUDevice`) — the escape hatch for WebGPU's async
`requestAdapter`/`requestDevice`, which is what lets `AddSingleton<GpuDevice, PixelyFactory>()`
stay synchronous. emdawnwebgpu preserves that entry point.

**It cannot be used on .NET 11**: `-s USE_WEBGPU` no longer exists past Emscripten 4.0.18, and
upstream has shipped no emdawnwebgpu variant. Under Option B, .NET 11 bindings would have to
be generated (ClangSharpPInvokeGenerator, or repointing Evergine's own generator) — 2–4 weeks
including an idiomatic layer over the new API's string views, futures and callback-info
structs. `Silk.NET.WebGPU` is not an alternative; its native package is `wgpu-native`, which
does not support Emscripten.

---

## 3. WGSL binding collisions — sizing, not a blocker

Tracked as [#480](https://github.com/stanoddly/Pixely/issues/480). An earlier draft of this
document ranked this as a hard blocker; that was wrong — a documented workaround exists. It
still sizes the WGSL work, and it applies under **both** options.

`SdlangCompiler.cs` requires a texture and its sampler to share a register index and space —
the SDL_GPU convention. `src/Pixely.Ui/Content/shaders/ui_quad.slang` does exactly that:

```slang
Texture2D<float4> texture      : register(t0, space2);
SamplerState textureSampler    : register(s0, space2);
```

Compiling it to WGSL with the bundled Slang 2026.17:

```
$ slangc ui_quad.slang -target wgsl -entry fragmentMain -stage fragment -o uq.wgsl
warning[E39001]: explicit binding overlap
note: see declaration of 'texture'

$ grep @group uq.wgsl
@binding(0) @group(3) var<uniform> uvRect_0 : vec4<f32>;
@binding(0) @group(2) var texture_0 : texture_2d<f32>;
@binding(0) @group(2) var textureSampler_0 : sampler;
@binding(1) @group(3) var<uniform> tintColor_0 : vec4<f32>;
```

`texture_0` and `textureSampler_0` both land on `@group(2) @binding(0)`. **WebGPU requires
binding numbers to be unique within a bind group**, so this WGSL is invalid.

Two aggravating details. First, the collision is *correct* for the existing targets — SPIR-V,
DXIL and MSL keep textures and samplers in separate descriptor namespaces, so nothing
overlaps there. Second, `SdlangCompiler.cs:207` and `:236` already pass
`-warnings-disable 39001,39013,39029`, and **39001 is exactly this warning**. Suppressing it
is right today and would hide a genuinely broken WGSL build.

The workaround: a full set of per-space, per-resource-class shifts, which preserves the
space→group mapping and separates the classes.

```
-fvk-b-shift 0 <space> -fvk-t-shift 10 <space> -fvk-s-shift 20 <space> -fvk-u-shift 30 <space>
```

for spaces 0–3 yields `uvRect @group(3) @binding(0)`, `tintColor @group(3) @binding(1)`,
`texture @group(2) @binding(10)`, `textureSampler @group(2) @binding(20)` — no warning.
**Partial shifts are a trap:** shifting only samplers resolves the collision but silently
re-assigns everything else to `@group(0)` with auto-allocated bindings.

So the WGSL arm is not "one more fan-out target", but it is tractable: per-target shift
configuration, keeping 39001 enabled for WGSL only, and metadata carrying the resulting
binding numbers. It also touches `ShaderFormatDto`, the JSON metadata, runtime shader-format
conversion, cache validation and cleanup, binding validation and the shader tests. Call it
**3–6 weeks**, not 2–3.

Related correction to an earlier draft of this document: `ShaderBindingLayout` is **not**
sufficient to build WebGPU bind group layouts. It stores category counts and some byte sizes.
`GPUBindGroupLayoutEntry` additionally needs a unique binding number and, per binding,
resource type, texture sample type and view dimension, sampler binding type, storage access
mode and format, whether the offset is dynamic, and a minimum binding size. The existing
metadata is a useful starting point, not the finished input.

---

## 4. Semantic gaps: SDL_GPU → WebGPU

Under **Option A these are SDL's problem** — #16020 has evidently solved them, since it passes
34/34 examples. Under Option B they are Pixely's, and they are the design work.

| SDL_GPU concept Pixely uses | WebGPU reality | Cost |
|---|---|---|
| `SDL_PushGPU{Vertex,Fragment,Compute}UniformData` — push constants, per draw, and the documented uniform path in `docs/render-pass-flow.md` | WebGPU has **no push constants**. Needs a per-frame ring uniform buffer, bind groups with dynamic offsets, 256-byte alignment, and preserved draw ordering. C# struct layout vs WGSL uniform layout also needs validating | **High** — the most invasive gap |
| `CopyPass`: 8 upload paths — **6 buffer, 2 texture**, no readback | `queue.writeBuffer` covers existing-buffer uploads, `mappedAtCreation` covers newly created ones; **neither covers the texture paths**, which need `queue.writeTexture` or buffer-to-texture copies. `mapAsync` is unusable single-threaded | **Medium** |
| `CommandBuffer.BlitTextures` — scales a full texture into another with filtering; the compute tutorial scales 512×512 into the swapchain | WebGPU `copyTextureToTexture` **does not scale or filter** and requires copy-compatible formats. Needs a dedicated blit render pipeline | **Medium** |
| `FillMode.Line` (public wireframe API) | WebGPU primitive state has **no polygon fill mode** at all | **Unsolvable** — must be documented as unsupported |
| `RasterizerState.EnableDepthClip = false` | Requires the optional `depth-clip-control` feature | **Low**, but feature-gated |
| `TextureFormat` — the full SDL pixel-format list | Many entries have no core-WebGPU equivalent; BC compression is behind optional `texture-compression-bc`, and mobile browsers commonly expose ETC2/ASTC instead | **Medium** — needs an explicit format/feature support policy, not a cast |
| `SDL_WaitAndAcquireGPUSwapchainTexture` (blocking, returns size) | `canvasContext.getCurrentTexture()` — synchronous, no wait; size comes from the canvas | **Low**; frame pacing moves to rAF |
| `GpuFence`, `SDL_WaitForGPUFences` | No fences; `queue.onSubmittedWorkDone` is a callback | **Medium** if anything blocks |
| Compute passes, storage buffers, read-write storage textures | Supported, tighter limits, no compute inside a render pass | **Low–Medium** |

---

## 5. Non-GPU subsystems

Costs assume .NET 11 and that the SDL3-port P/Invoke question resolves favourably. If it does
not, every "keep SDL" row becomes a reimplementation over `[JSImport]` and this section
roughly doubles.

| Subsystem | Today | Browser answer | Complexity |
|---|---|---|---|
| Event loop | `while (true)` in `PixelyApp.Run()`; `IPixelyApp` exposes synchronous `int Run()` | **A blocking loop hangs the tab**, SDL port or not. Needs `emscripten_set_main_loop` / SDL main callbacks / rAF driving a `Frame()` step, and a changed host contract | Medium — **applies to both options** |
| File dialogs | `SDL_ShowOpenFileDialog` + a `SDL_PumpEvents`/`SDL_Delay(10)` **spin-wait** (`Window.cs:542`) | `<input type=file>` / `showOpenFilePicker`, both async. The spin-wait deadlocks the browser | Low code, but a sync API has to become async |
| Window | `SDL_CreateWindow`, hit test, shape, icon, fullscreen | **Keep SDL.** But **multi-window (`WindowRegistry`, `ViewScope`), transparency, borderless, always-on-top, `SetWindowShape`, `SetWindowPosition`, drag hit-test have no browser meaning** — no-ops, mirroring how `PlatformInfo` already gates Wayland | Low–Medium; API-shape decisions |
| Input | SDL event pump → keyboard/mouse/gamepad/text-input | **Keep SDL** — the port maps DOM events to `SDL_Event` | Low |
| Clipboard | `SDL_GetClipboardText` (sync) | `navigator.clipboard` is **async and permission-gated**; no shim makes it synchronous. `IClipboardService` is sync today | Medium — API-shape change |
| Text | SDL_ttf → `SDL_Surface` | **Keep SDL_ttf** — `sdl3_ttf` port exists, and TTF only rasterises to a surface, so SDL_GPU is not involved | Low |
| Images | `SdlImageLoader` (SDL_image) | **No `sdl3_image` port.** `createImageBitmap` is **async** while `IImageLoader.Load` is sync — so either a managed synchronous decoder or a preload scheme, or the interface changes | Low–Medium |
| Audio | SDL_mixer (`Pixely.Audio`, 1 090 lines) | **No `sdl3_mixer` port.** Build it for wasm, retarget onto SDL3's own audio, or Web Audio. Also needs a user-gesture unlock and async decoding. `IAudioSystem` exists but returns SDL-backed concrete types | Medium |
| Content | `Directory.GetFiles` / `File.OpenRead` over `AppContext.BaseDirectory`; ZIP | `EmbeddedContentSource` works unchanged. **`ZipContentSource.Create` only takes a filesystem path** and its `ZipArchive` constructor is private, so a fetched archive needs a new stream overload or VFS staging. (The default archive extension is `.pk3`.) `DirectoryContentSource` and the `AddDefaultContent` probing do not work | Low–Medium |

---

## 6. Blockers, ranked

1. **SDL_GPU has no *merged* browser backend.** `src/gpu` on main is `d3d12`/`metal`/`vulkan`/`xr`;
   `release-3.4.2` is `d3d12`/`metal`/`vulkan`. PR #16020 is open, unmerged, 34/34 examples
   passing, author on hiatus, no date. *This is now a scheduling risk rather than a hard
   blocker — but it is someone else's schedule.*
2. **`while (true)` in `PixelyApp.Run()`** and the file-dialog spin-wait in `Window.cs` — hang
   the tab. Changes `IPixelyApp`'s contract. Applies under **both** options. *The only
   unavoidable code-level blocker that is entirely within Pixely's control.*
3. **Unverified: can .NET wasm P/Invoke into `--use-port=sdl3`?** Gates Option A entirely.
4. **No push constants in WebGPU** while `Push*UniformData` is Pixely's documented uniform
   path. Option B only; solvable with a uniform ring buffer.
5. **Features with no WebGPU equivalent**: `FillMode.Line`, much of `TextureFormat`, filtered
   `BlitTextures`, fences. Needs a published support matrix, not a port.
6. **No `sdl3_image` / `sdl3_mixer` ports**, and both browser replacements are async behind
   sync Pixely interfaces.
7. **No multi-window in a browser.** `WindowRegistry`/`ViewScope` and several tutorials have
   no browser answer. Scope reduction, not a blocker.
8. **WGSL binding shifts** ([#480](https://github.com/stanoddly/Pixely/issues/480)) — a
   configuration gap with a known workaround, listed for completeness. Sizes the WGSL work;
   does not block it.
9. **No browser publish/AOT/trim test exists.** The DI graph is expected to trim cleanly;
   nothing proves it.

---

## 7. Scoping the estimate

"Runs the existing tutorials" is not a deliverable — the repo has multi-window, transparent,
click-through, dragging, taskbar-icon, message-box, file-dialog, always-on-top and clipboard
tutorials that cannot keep their behaviour in a browser. Any estimate needs a named subset.

**Proposed browser support matrix, v1:** Triangle, IndexBuffer, IndexedRenderPass, Instancing,
StorageBuffer, TextureArray, DepthOnly, StencilBuffer, ImageLoading, EmbeddedContent,
UiBoxes, UiScoreboard, UiTextInput, Hotbar, StageSwitching. **Explicitly out:** MultiWindow,
MultiWindowTextInput, TransparentWindow, ClickThrough, WindowDragging, WindowConfiguration,
TaskbarIcon, MessageBoxes, FileDialogs, MouseWindowPresence, ZipContent. **Deferred:** Audio,
ComputeShader (needs the blit path).

Against that matrix:

| | Option A (on SDL #16020) | Option B (own backend) |
|---|---|---|
| WGSL shader arm + binding convention | 3–6 wk | 3–6 wk |
| Main-loop / host contract | 2–3 wk | 2–3 wk |
| SDL3-port binding + wasm build plumbing | 3–5 wk | n/a |
| GPU backend seam + enum decoupling + uniform ring | — | 10–16 wk |
| emdawnwebgpu .NET bindings | — | 2–4 wk |
| WebGPU backend incl. blit path, format policy | — | 12–18 wk |
| Browser host (image, audio, content, clipboard, dialogs) | 6–10 wk | 8–14 wk |
| Packaging, RID-conditional refs, CI, browser tutorial | 3–4 wk | 4–5 wk |
| Cross-browser validation contingency | 3–4 wk | 5–8 wk |
| **Total** | **~20–32 wk**, gated on #16020 merging | **~46–74 wk** |

Both figures assume one engineer who knows the codebase, and neither includes making the
excluded tutorials degrade gracefully.

---

## 8. Recommended next steps

Three cheap, independent probes. None commits to either option; all three are needed under
both, or answer a question that picks between them.

1. **(~1 week) Fix the WGSL binding convention.** Get `ui_quad.slang` and one compute shader
   emitting valid, collision-free WGSL from the bundled Slang, and work out what the metadata
   has to carry — the shift flags in §3 are the starting point. Tracked as
   [#480](https://github.com/stanoddly/Pixely/issues/480); entirely local, and on the critical
   path for both options.
2. **(~1 week) Prove .NET wasm can call the Emscripten SDL3 port.** A `net11.0` /
   `browser-wasm` app that P/Invokes `SDL_Init` + `SDL_CreateWindow` against
   `--use-port=sdl3`, ideally through `ppy.SDL3-CS`'s own declarations. This is blocker #3 and
   it decides whether Option A is a port or a rewrite.
3. **(~2 days) Open a conversation on SDL #16020.** Ask about merge intent and whether help is
   wanted. The single largest variable in this document is that PR's timeline, and the
   maintainers are engaged right now.

Then re-decide. If #16020 merges, Option A is clearly right. If it stalls for two or more
quarters, revisit Option B — and start it with the GPU backend seam, which is worth doing for
API honesty even if the browser target never ships.

---

## Sources

- [SDL PR #16020 — GPU: Experimental WebGPU SDLGPU Backend (open draft)](https://github.com/libsdl-org/SDL/pull/16020)
- [SDL PR #12046 — earlier community WebGPU backend (closed 2025-03-16)](https://github.com/libsdl-org/SDL/pull/12046)
- [SDL issue #10768 — WebGPU backend tracking](https://github.com/libsdl-org/SDL/issues/10768)
- [libsdl-org/SDL `src/gpu` on main](https://api.github.com/repos/libsdl-org/SDL/contents/src/gpu) and [at `release-3.4.2`](https://api.github.com/repos/libsdl-org/SDL/contents/src/gpu?ref=release-3.4.2)
- [dotnet/runtime `release/10.0` eng/Versions.props](https://raw.githubusercontent.com/dotnet/runtime/release/10.0/eng/Versions.props)
- [dotnet/runtime `release/11.0-preview7` eng/Versions.props](https://raw.githubusercontent.com/dotnet/runtime/release/11.0-preview7/eng/Versions.props)
- [Emscripten ChangeLog (4.0.10, 4.0.18, 5.0.3, 5.0.5)](https://github.com/emscripten-core/emscripten/blob/main/ChangeLog.md)
- [Emscripten `tools/ports` at 6.0.2](https://api.github.com/repos/emscripten-core/emscripten/contents/tools/ports?ref=6.0.2)
- [Emscripten emdawnwebgpu port](https://github.com/emscripten-core/emscripten/blob/main/tools/ports/emdawnwebgpu.py)
- [Emdawnwebgpu README (Dawn)](https://dawn.googlesource.com/dawn/+/refs/heads/main/src/emdawnwebgpu/pkg/README.md)
- [Evergine.Bindings.WebGPU on NuGet](https://www.nuget.org/packages/Evergine.Bindings.WebGPU)
- [WebGPU spec — `GPUBindGroupLayoutEntry`](https://www.w3.org/TR/webgpu/#dictdef-gpubindgrouplayoutentry)
- [WebGPU spec — `copyTextureToTexture`](https://www.w3.org/TR/webgpu/#dom-gpucommandencoder-copytexturetotexture)
- [Silk.NET.WebGPU on NuGet](https://www.nuget.org/packages/Silk.NET.WebGPU)
