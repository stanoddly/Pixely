# Browser target, proof of concept

Pixely running in a browser on SDL_GPU's WebGPU backend, compiled to WebAssembly.

**This is a proof of concept, not a supported target.** It depends on an unmerged SDL pull
request that needed patching, a stubbed-out set of features, and hand-built native archives.
`wasm-poc.md` in the repository root is the full record: what was tried, what failed, and every
hack that is holding it up.

These scripts are session artifacts, lightly parameterised. They are the honest record of how
the result was produced rather than polished tooling, and they assume a Linux host with the
.NET SDK under `~/.dotnet`.

## Prerequisites

- .NET 11 SDK with the `wasm-tools` and `wasm-experimental` workloads. The workload packs
  supply Emscripten, so no separate emsdk is needed. Despite the pack being named 6.0.2, the
  Emscripten inside it reports **6.0.3**.
- cmake, ninja, git, python3.
- A browser with WebGPU on a hardware adapter, plus Node for the test harness.

## Where the work happens

Everything is built outside the repository, in a directory named by `PIXELY_WASM_WORK`,
defaulting to `~/.pixely-wasm`. The tutorial projects read the same variable to find the
native archives.

    export PIXELY_WASM_WORK=~/.pixely-wasm
    mkdir -p "$PIXELY_WASM_WORK"

## Build order

1. **SDL3, from the fork carrying [PR #16020](https://github.com/libsdl-org/SDL/pull/16020).**

       cd "$PIXELY_WASM_WORK"
       git clone --depth 1 --single-branch --branch main https://github.com/TheeStickmahn/SDL_wgpu.git
       git -C SDL_wgpu apply <repo>/wasm/patches/sdl-webgpu-pixely.patch
       <repo>/wasm/scripts/configure.sh
       <repo>/wasm/scripts/rebuild-sdl.sh

   `patches/sdl-webgpu-pixely.patch` is not optional. It carries three bug fixes plus one
   diagnostics change; see below.

2. **SDL3_ttf and SDL3_image**, built against that fork.

       <repo>/wasm/scripts/fetch-ttf-deps.sh
       <repo>/wasm/scripts/build-ttf.sh && <repo>/wasm/scripts/make-ttf.sh
       <repo>/wasm/scripts/build-image.sh && <repo>/wasm/scripts/make-image.sh

3. **The boot shim.**

       <repo>/wasm/scripts/build-shim.sh

4. **The tutorials.**

       dotnet publish -c Release tutorials/Pixely.Tutorials.ImageTextBrowser
       dotnet publish -c Release tutorials/Pixely.Tutorials.TriangleBrowser

## Things that will waste your time otherwise

- **An archive's file name is its P/Invoke module name.** `libSDL3.a` registers as module
  `libSDL3`, so `DllImport("SDL3")` silently resolves to nothing. The archives must be named
  `SDL3.a`, `SDL3_ttf.a`, `SDL3_image.a`.
- **The SDK's Emscripten cache is frozen and read-only**, so `--use-port=` fails with "Attempt
  to lock the cache but FROZEN_CACHE is set". Pointing `WasmCachePath` anywhere else flips
  `EM_FROZEN_CACHE` to 0. Note `FROZEN_CACHE` is read as `bool(os.getenv(...))`, so `"0"` still
  means frozen; only an empty string disables it.
- **Path spelling matters.** Use `/var/home/...`, never the `/home/...` symlink, or Emscripten's
  sanity check sees a changed config and wipes the cache mid-build.
- **The freetype and harfbuzz Emscripten ports cannot be used.** `tools/ports/freetype.py:21-25`
  returns the `-legacysjlj` variant for any `SUPPORT_LONGJMP=wasm` link and ignores
  `WASM_LEGACY_EXCEPTIONS`. Since .NET links `-fwasm-exceptions -sWASM_LEGACY_EXCEPTIONS=0`, the
  browser rejects the module: "module uses a mix of legacy and new exception handling
  instructions". Both are vendored into `SDL3_ttf.a` instead.
- **Vertex winding is load-bearing.** Reversing a quad's vertex order culls the triangles
  silently, with no validation message.

## Why there is a C shim

`SDL_CreateGPUDevice` busy-waits on the WebGPU future and calls `emscripten_sleep`, so the wasm
stack has to suspend. Asyncify cannot do it: it wraps every wasm export in a JS wrapper, which
destroys Mono's `cwrap` arities and function-table writes, and the runtime dies during startup.
JSPI can, because it wraps only the exports named in `JSPI_EXPORTS`.

But a JSPI suspension unwinds to the nearest `WebAssembly.promising` frame, and Mono's entry
export is not promising, so a call starting in managed code has no boundary to suspend to. The
blocking calls therefore live in `shim/pixelyboot.c`, entered from JavaScript before managed
code runs, and Pixely adopts the resulting device and window.

Only device creation needs this. Per-frame rendering never suspends.

The proper fix is upstream: if SDL's WebGPU backend accepted a preinitialized device, the way
Emscripten's older WebGPU binding did through `Module.preinitializedWebGPUDevice`, no suspension
would be needed and the shim would disappear.

## The SDL patch

Three bugs, all of which only appear once something renders continuously, which is why the PR's
own examples do not hit them:

- `WEBGPU_AcquireSwapchainTexture` never reaped submitted command buffers. Nothing else
  decrements `submittedCommandBufferCount`, so the non-blocking acquire returned NULL forever
  from frame two.
- `WEBGPU_Cancel`'s entire body was `SDL_assert_release(!"...I'm lazy.")`.
- `WEBGPU_INTERNAL_ParseBindGroupLayoutEntriesFromShader` scans for `@group`, `@binding`, `var`
  in that fixed order, but WGSL does not fix attribute order and Slang emits
  `@binding(0) @group(2) var`. It took one attribute from the texture and the other from the
  sampler, merged them into a single entry, lost the texture, and Dawn rejected the pipeline.

The fourth hunk makes the error and device-lost callbacks print their reason and message. That
is diagnostics, not a fix, but without it the device-loss failure below is invisible.

## Known failure

On Chromium's SwiftShader adapter the WebGPU device is lost right after the first presented
frame, and SDL's recreate path then busy-waits inside a WebGPU callback and throws
`SuspendError`. It is the adapter, not headless mode: the same binary runs 300+ frames on
Vulkan. This matters for CI, where a software adapter is often all there is.

## Testing in a browser

`harness/` drives headless Chromium and reports console output, page errors and adapter status.

    cd wasm/harness && npm install playwright && npx playwright install chromium
    node run-page.js http://localhost:8700/ shot.png 9000

`webgpu-flag-matrix.js` works out which flags expose WebGPU on a given machine, and
`vanilla-check.js` reports what a browser supports with no flags at all. JSPI is on by default
in current Chromium and needs no flag; WebGPU on Linux is often gated behind
`--enable-features=Vulkan`. Avoid `--enable-unsafe-webgpu` on its own, since it selects
SwiftShader and hits the failure above.
