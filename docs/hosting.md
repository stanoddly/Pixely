# Hosting

Pixely can own the entry point. A project that opts in gets a generated `Main` and writes only what
to register. The same project runs on the desktop with `dotnet run` and in a browser with
`dotnet publish -r browser-wasm`.

## Opting in

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <Sdk Name="Pixely" Version="0.0.N" />

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net11.0</TargetFramework>
    <PixelyHosting>true</PixelyHosting>
  </PropertyGroup>
</Project>
```

The `<Sdk>` line brings the generator (see [SDK](sdk.md)); the property is the whole opt-in. Without it
nothing is generated and the project writes its own `Main`; every other Pixely API works either way.

## What the project writes

`Program` is a `static partial class` in the project's `RootNamespace` and declares `Configure`:

```csharp
namespace Foo;

static partial class Program
{
    static void Configure(PixelyAppBuilder builder)
    {
        builder.UseDefaultContent().UseDefaultRendering(new WindowConfig(Size: (1280, 720), Title: "Game"));
        builder.AddSingleton<IRenderer<BasicRenderContext>>(TriangleRenderer.Create);
    }
}
```

- `RootNamespace` defaults to the project file name and is where `dotnet new` puts `Program`. A project
  that wants `Program` elsewhere does not opt in.
- `Configure` runs before `Build()`. It is the only place that registers into the root container, and nothing after it sees the app object: work
  that needs built services runs in `OnBuilt` (`docs/class-registration.md`), a stage is loaded
  through `IStageManager.Load`, and a registered `UiView` is added to its root by `Pixely.Ui`
  (`docs/ui.md`).
- Command-line arguments come from `Environment.GetCommandLineArgs()`; its first element is the
  executable.
- `Configure` and `OnException` are ordinary private methods. The generated `Main` is another part of
  the same class, so nothing needs to be `internal` or `partial` beyond the class itself.
- A missing `Configure` is CS0117: the generated `Main` names `Program.Configure`.

## Reporting failures

Pixely does not report failures on its own. A project that wants a player-visible message declares
the optional `OnException`, whose return value is the exit code:

```csharp
static int OnException(Exception exception)
{
    Console.Error.WriteLine(exception);
    MessageBox.Show(MessageBoxSeverity.Error, "Fatal error", exception.Message);
    return 1;
}
```

A failure in `Configure`, `Build` or `Run` is passed to it before the application is disposed: a
failure during `Run` still has the window open behind the box, a failure during `Build` has no window
yet. An exception thrown by the handler itself propagates in place of the original.

When the project declares none, the generated file's default applies: it rethrows the exception with
its original stack trace, so the process fails as it would from a hand-written `Main`. The default is
an extension member of `Program`, and lookup prefers a member the type declares over an extension
member, which is what makes `OnException` optional without a declaration of any kind.

The one consequence: the default also applies when the declared method is not applicable to an
`Exception` argument. `static int OnException(InvalidOperationException exception)` compiles and never
runs. A wrong return type still fails to compile: the generated `Main` invokes `OnException` instead of
passing it as a method group, and an invocation binds to the declared method whatever it returns.

## The generated file

`obj/<Configuration>/<TargetFramework>/PixelyProgram.g.cs`, with a `<RuntimeIdentifier>` segment
after the framework when the build has one, shown in the IDE under `Properties/PixelyProgram.g.cs`.
It adds `Main` to `Program`, which builds the app from `Configure`, runs it, hands a failure to
`OnException` and disposes the app last, and `PixelyProgramDefaults` to the namespace, which holds the
default `OnException`. Nothing else is added. On the desktop `Main` is `static int Main()` and calls
`app.Run()`; for `browser-wasm` it is `static async Task<int> Main()`, marked
`[SupportedOSPlatform("browser")]`, and awaits `BrowserHost.RunAsync(app)`. Every other line is the
same. The generated part of `Program` states no accessibility, so the project's part
may state any. The file is generated for `Exe` and `WinExe` C# projects, is rewritten only when its
content changes, and is removed by `dotnet clean`.

## Diagnostics

- CS0117: `Configure` is not declared.
- CS0123 or CS0407: `Configure` has the wrong parameter or return types.
- CS0029: `OnException` does not return `int`.
- CS0017: the project has another `Main`. Remove it or do not opt in.
- CS0260: the project's `Program` is not `partial`.
- CS7022: the project uses top-level statements, which take precedence over the generated `Main`.
  Do not opt in.
- An error naming `namespace <name>;` in the generated file: the project file name is not a valid
  identifier. Set `RootNamespace` in the project.
- PIXELY0004: `RuntimeIdentifier` is `browser-wasm` in the project body. Pass `-r browser-wasm`
  instead.
- PIXELY0007: a browser build whose framework is not a browser one. The SDK switches a single
  `net11.0` without `-f`; a project with `TargetFrameworks` lists `net11.0-browser` itself and
  publishes with `dotnet publish -f net11.0-browser -r browser-wasm` (see below).

## The browser

```sh
dotnet publish -r browser-wasm -c Release
```

The Pixely SDK sees `RuntimeIdentifier` at SDK-import time and layers `Microsoft.NET.Sdk.WebAssembly`
on top of `Microsoft.NET.Sdk`, so the RID must be a global property, the `-r` switch. Set in the
project body it is invisible to the SDK props and the build fails with PIXELY0004. Only an executable
becomes a browser app: a project whose `OutputType` is not `Exe` or `WinExe` gets neither the
WebAssembly pack nor its `SelfContained` and `PublishTrimmed` defaults, whether it is restored as a
reference of the browser app or published with the RID itself, so a solution-wide
`dotnet publish -r browser-wasm` publishes the executables for the browser and the libraries as
libraries. Prefer `-r` on the executable project all the same; a RID in `Directory.Build.props` makes
every desktop build of the repository a browser build. Without native references no `wasm-tools`
workload is needed, for publishing or for `dotnet run -r browser-wasm`, which serves the app from a
local host; with them the runtime is relinked and the workload is required (see below).

### The browser target framework

A browser app targets `net11.0-browser`. Browser-specific code in Pixely is selected at compile time,
so the package carries a browser build of the library under `lib/net11.0-browser1.0/` (NuGet's folder
name carries the platform version) and the desktop build under `lib/net11.0/`, and NuGet hands a
project one folder, by its target framework. The project keeps its one `TargetFramework` line: with
`-r browser-wasm` the SDK switches `net11.0` to `net11.0-browser` after the project body, restore reads
the switched value, and the compiler gets the `BROWSER` symbol, so `#if BROWSER` selects the project's
own browser code. The switch is late: the project body and every props file evaluate with `net11.0`,
so a condition on `TargetFramework` in the project body does not see a browser publish; condition on
`RuntimeIdentifier` (a global property, `browser-wasm`) instead.

A project that multi-targets is not switched: it lists `net11.0-browser` in `TargetFrameworks` itself
and publishes that inner build with `dotnet publish -f net11.0-browser -r browser-wasm`. Without `-f`
every inner build gets the RID, and one whose framework is not a browser one fails with PIXELY0007,
as does a list without the browser framework. `net11.0-browser1.0`, the same framework with its
platform version spelled out, is accepted wherever `net11.0-browser` is. Every Pixely assembly has a
`net11.0-browser` copy, so `Pixely.Ui` and the others bind to the browser `Pixely.dll` at run time;
assembly identity is name and version, not framework, which is why the browser build's public
surface is kept a superset of the desktop one (package validation checks it at pack).

The page is under `bin/<Configuration>/net11.0-browser/browser-wasm/publish/wwwroot/`. Serve that
directory over HTTP; opening `index.html` from disk does not work. The Publish SDK also copies the
project's `*.json` and `*.config` files, `global.json` and `NuGet.Config` included, beside `wwwroot`,
so serve `wwwroot` only. Desktop and browser keep separate `obj/` and `bin/` directories, but they
share the restore state: after a browser restore the WebAssembly pack's props default the RID to
`browser-wasm`, so an evaluation without `-r` and without a restore (`--no-restore`, `dotnet msbuild`,
a design-time build) is a browser build, `net11.0-browser` included, until the next desktop restore.
`dotnet build` and `dotnet run` restore first.

For the browser the SDK forces `PublishAot=false`, `SelfContained=true` and `PublishTrimmed=true`;
the project's own values apply to the desktop. Trim analysis warnings stay on.

### SDL3 in the browser

The .NET runtime for the browser contains no SDL, and Pixely's native calls (`DllImport("SDL3")`,
`"SDL3_image"`, `"SDL3_ttf"`, `"SDL3_mixer"`) fail with `DllNotFoundException` until SDL is linked
into `dotnet.native.wasm`. That is a relink of the runtime, which needs the `wasm-tools` workload
(`dotnet workload install wasm-tools`) and takes Emscripten static libraries as ordinary
`NativeFileReference` items:

```xml
<ItemGroup>
  <NativeFileReference Include="wasm/SDL3.a" />
  <NativeFileReference Include="wasm/SDL3_image.a" />
</ItemGroup>
```

The file name matters: the wasm build registers each native reference under its file name as a
P/Invoke module and matches `DllImport` names against it literally (.NET 11, Mono runtime), so the
archive for `DllImport("SDL3")` is `SDL3.a`, not `libSDL3.a`, and so on for `SDL3_image.a`,
`SDL3_ttf.a` and `SDL3_mixer.a`. Any further archive those libraries need (FreeType, HarfBuzz, libpng,
Ogg, Vorbis) is one more `NativeFileReference`; the runtime already links zlib. A reachable native
call whose symbol no archive provides fails the link (`WasmAllowUndefinedSymbols=true` defers that to
run time); a library that is not linked at all leaves its calls failing at run time as before. The
Pixely SDK adds no link step of its own; a relink happens whenever native references exist. Only a publish
trims, so `dotnet build -r browser-wasm` and `dotnet run -r browser-wasm` put every P/Invoke of the
SDL bindings in the table, and an archive built from an older SDL than the bindings target fails
the link on the calls it lacks; set `WasmAllowUndefinedSymbols` to `true` for those commands.

Getting the archives: Emscripten has ports for SDL3 (3.4.2 in the Emscripten .NET 11 bundles, so
`SDL_WINDOW_FILL_DOCUMENT` works) and `sdl3_ttf`, but a port is fetched and compiled into the
Emscripten cache at link time and the workload's cache is frozen, so `--use-port=sdl3` fails in a
normal build. Linking the port directly would not bind the calls anyway: the P/Invoke table is keyed
by native-reference file names, and a port adds none (the SDK warns PIXELY0005 about the related
mistake of a `libSDL3*.a` reference). So the port is a way to build the archive once:

1. Copy the workload's cache (`packs/Microsoft.NET.Runtime.Emscripten.*.Cache.*/<version>/tools/emscripten/cache`) to a writable directory.
2. Run the workload's `emcc --use-port=sdl3` on any C file with `EM_CACHE` set to that copy and `EM_FROZEN_CACHE=0` (the workload's `emcc` reads `DOTNET_EMSCRIPTEN_LLVM_ROOT`, `DOTNET_EMSCRIPTEN_BINARYEN_ROOT` and `DOTNET_EMSCRIPTEN_NODE_JS` for its toolchain, `packs/Microsoft.NET.Runtime.Emscripten.*.Sdk.*/<version>/tools/bin`, `.../tools` and the Node pack's `tools/bin/node`). Pointing a project's `WasmCachePath` at the copy and adding `--use-port=sdl3` to `EmccExtraLDFlags` builds the port the same way during a publish.
3. Copy `sysroot/lib/wasm32-emscripten/libSDL3.a` out of the cache as `SDL3.a` and reference it.

The satellite libraries and their dependencies come from their own Emscripten builds the same way.

`tutorials/Pixely.Tutorials.Browser` links every `*.a` in `BrowserNativeLibraryDirectory`:
`dotnet publish -r browser-wasm -c Release -p:BrowserNativeLibraryDirectory=/path/to/archives`.
With the port's `SDL3.a` alone the window fills the page and `ResolutionChanged` fires; the GPU
needs the WebGPU backend below.

### WebGPU in the browser

SDL has no WebGPU backend in its releases. Pixely uses the one in the
[stanoddly/SDL_wgpu](https://github.com/stanoddly/SDL_wgpu) fork, branch `webgpu-fixes`, on top of
the upstream pull request libsdl-org/SDL#16020. `SDL3.a` is built from that fork with
`-DSDL_WEBGPU=ON -DSDL_WEBGPU_EMSCRIPTEN=ON` under the workload's Emscripten (the recipe above, with
`emcmake cmake` and `cmake --build --target SDL3-static` in place of the port), and takes the place
of the port's archive. `UseGpu()` creates the font system too, so `SDL3_ttf.a` is linked with it,
built against the fork's build directory (`SDL3_DIR`) with `SDLTTF_VENDORED=ON`: the Emscripten
FreeType port is built with legacy exception instructions, which the .NET link rejects.

The backend is implemented on emdawnwebgpu, the WebGPU binding Emscripten ships as a remote port.
The publish opts into it with `PixelyBrowserWebGpu=true`, which adds `--use-port=emdawnwebgpu` to
the link, exports the entry points `pixely-host.js` uses, and requires `WasmCachePath` to name a
writable copy of the workload's Emscripten cache (PIXELY0006 otherwise), because the port is fetched
and built into the cache at link time. Building `SDL3.a` against that same copy builds the port
once. The whole publish:

```shell
dotnet publish -r browser-wasm -c Release -p:BrowserNativeLibraryDirectory=/path/to/archives -p:PixelyBrowserWebGpu=true -p:WasmCachePath=/path/to/emcache
```

Requesting a WebGPU adapter and device is asynchronous, and SDL would wait it out by suspending the
wasm stack, which no managed frame survives. So the page requests them: the generated `Main` awaits
`BrowserHost.PrepareAsync(builder)` before `Build()`, and when the builder has a `GpuDevice`
registered, `pixely-host.js` requests an adapter, requests a device with the features SDL requires,
imports both into emdawnwebgpu beneath an instance of its own, and `PixelyFactory` hands the three
pointers to `SDL_CreateGPUDeviceWithProperties`, which adopts them without waiting. A hand-written
`Main` awaits `PrepareAsync` the same way. `GpuBackend.Automatic` advertises WGSL only in the
browser; `GpuBackend.WebGpu` (`PIXELY_GRAPHICS=webgpu`) names the driver explicitly.

SDL cannot install callbacks on an adopted device, so `pixely-host.js` observes `device.lost` and
`uncapturederror` itself: an error is logged, and a lost device ends the frame loop with the loss
as the exception, since SDL would keep recording against the dead device without noticing.

Destroying SDL's device spins, without yielding, until every submission has completed, and a
submission completes only after the page's event loop turns, which a synchronous `Dispose` cannot
wait for. So `RunAsync` awaits `queue.onSubmittedWorkDone()` once the frame loop has ended, on the
exception path too, and `GpuDevice.Dispose` then destroys the SDL device as on the desktop, after
which `pixely-host.js` releases the imported handles and destroys the WebGPU device. A `Build()`
that fails once the device exists, because SDL rejected the handles or a later singleton threw,
releases the page's device the same way; one that fails before the device is resolved leaves it
until the page unloads or the next `PrepareAsync`, which releases it first.

Not supported in the browser, each throwing `PlatformNotSupportedException`: `GpuDevice.WaitForFences`
and `CommandBuffer.SubmitAndDownloadTexture` (SDL's wait and download mapping suspend the wasm
stack under a managed frame). Headless mode (`PixelyConfig.Headless`, `PIXELY_HEADLESS`) reads
commands from standard input and frames back through that download, so a headless app fails at
`Build()` with `PixelyInitializationException` in the browser. The swapchain is acquired
with the non-waiting `SDL_AcquireGPUSwapchainTexture`, so a frame with no texture ready is skipped;
`requestAnimationFrame` paces the frames anyway.

Shaders reach the browser as the WGSL the shader compiler emits beside SPIR-V, DXIL and MSL (see
[Shaders](shaders.md), WGSL bindings). A content directory beside the executable has no browser
counterpart yet, so the tutorial embeds the generated shaders in the assembly, on both hosts.

### The frame loop

A browser owns the frame loop, so `Pixely.App.BrowserHost.RunAsync(app)` takes the place of
`app.Run()`: `pixely-host.js` calls `IPixelyApp.RunFrame()` once per `requestAnimationFrame` and
`RunAsync` completes with 0 when it returns `false`. An exception thrown by a frame rejects the loop
and is rethrown by `RunAsync` as the original managed exception, so the generated `catch`,
`OnException` and the `finally` that disposes the app run as on the desktop. `BrowserHost` is public
for a hand-written `Main`, which is `async Task<int>` and carries `[SupportedOSPlatform("browser")]`;
without the attribute CA1416 fires on the browser compile. The desktop build of Pixely declares
`BrowserHost` too, so the public surface is the same in both builds; there `RunAsync` throws
`PlatformNotSupportedException`.

`dotnet.runMain()` in `main.js` resolves with the value `Main` returns. When `OnException` returns,
that value is the result; when it throws, including the default that rethrows, `runMain()` rejects
and `main.js` logs the error and rethrows it to the browser console. What `MessageBox.Show` does in
a handler depends on the native SDL build, which is a separate piece of work.

In a browser the page is the screen: the window fills it and follows the browser window's size, so
`WindowConfig.Size` is ignored, as are `Fullscreen`, `Resizable`, `Transparent`, `Borderless` and
`AlwaysOnTop`. `Window.Size` reports the page size and resizes arrive through `ResolutionChanged` as
on the desktop. `UseDefaultContent()` and file logging are not supported in the browser yet.

### The page

The package ships three static web assets and adds each to the project only when the project's
`wwwroot/` has no file at that relative path:

- `index.html`: `<canvas id="canvas">`, the element SDL's Emscripten port draws into, a full-page
  stylesheet and `<script type="module" src="main.js">`.
- `main.js`: imports `./_framework/dotnet.js`, passes the canvas as `Module.canvas`, awaits
  `dotnet.runMain()`, logs the exit code, and logs and rethrows a rejection.
- `pixely-host.js`: exports `runFrameLoop(runFrame)`, `createGpuDevice()`, `waitForGpuIdle()` and
  `releaseGpuDevice()`, which `BrowserHost` imports as module `pixely-host` from
  `../pixely-host.js`, relative to `dotnet.js`. A replacement keeps the exports and the location.

A project's own `wwwroot/index.html` or `wwwroot/main.js` replaces the default with no further
setting. `PixelyBrowserIndexHtml` and `PixelyBrowserMainJs` point the default at another file;
`PixelyBrowserDefaultAssets=false` adds none of the three. The check is by file: an `index.html`
that reaches `wwwroot/` as a linked `Content` item or generated content collides with the default,
so such a project sets `PixelyBrowserDefaultAssets=false`.

`tutorials/Pixely.Tutorials.Browser` is the smallest example: `UseDefaultRendering` and one renderer
drawing a magenta quad from embedded shaders, logging `ResolutionChanged`. On the desktop it runs as
any other tutorial (see [Window rendering](window-rendering.md)); in the browser it needs the WebGPU
`SDL3.a`, `SDL3_ttf.a` and `PixelyBrowserWebGpu` as described above.
