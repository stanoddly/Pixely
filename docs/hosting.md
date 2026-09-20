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
every desktop build of the repository a browser build. No `wasm-tools` workload is needed while
nothing native is linked, for publishing or for `dotnet run -r browser-wasm`, which serves the app
from a local host.

The page is under `bin/<Configuration>/net11.0/browser-wasm/publish/wwwroot/`. Serve that directory
over HTTP; opening `index.html` from disk does not work. The Publish SDK also copies the project's
`*.json` and `*.config` files, `global.json` and `NuGet.Config` included, beside `wwwroot`, so serve
`wwwroot` only. Desktop and browser keep separate `obj/` and `bin/` directories, but they share the
restore state: after a browser restore the WebAssembly pack's props default the RID to `browser-wasm`,
so an evaluation without `-r` and without a restore (`--no-restore`, `dotnet msbuild`, a design-time
build) is a browser build until the next desktop restore. `dotnet build` and `dotnet run` restore first.

For the browser the SDK forces `PublishAot=false`, `SelfContained=true` and `PublishTrimmed=true`;
the project's own values apply to the desktop. Trim analysis warnings stay on.

### The frame loop

A browser owns the frame loop, so `Pixely.App.BrowserHost.RunAsync(app)` takes the place of
`app.Run()`: `pixely-host.js` calls `IPixelyApp.RunFrame()` once per `requestAnimationFrame` and
`RunAsync` completes with 0 when it returns `false`. An exception thrown by a frame rejects the loop
and is rethrown by `RunAsync` as the original managed exception, so the generated `catch`,
`OnException` and the `finally` that disposes the app run as on the desktop. `BrowserHost` is public
for a hand-written `Main`, which is `async Task<int>` and carries `[SupportedOSPlatform("browser")]`;
without the attribute CA1416 fires on the browser compile.

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
- `pixely-host.js`: exports `runFrameLoop(runFrame)`, which `BrowserHost` imports as module
  `pixely-host` from `../pixely-host.js`, relative to `dotnet.js`. A replacement keeps the export and
  the location.

A project's own `wwwroot/index.html` or `wwwroot/main.js` replaces the default with no further
setting. `PixelyBrowserIndexHtml` and `PixelyBrowserMainJs` point the default at another file;
`PixelyBrowserDefaultAssets=false` adds none of the three. The check is by file: an `index.html`
that reaches `wwwroot/` as a linked `Content` item or generated content collides with the default,
so such a project sets `PixelyBrowserDefaultAssets=false`.

`tutorials/Pixely.Tutorials.Browser` is the smallest example: `AddWindow` only, no GPU, it logs
`ResolutionChanged`. On the desktop a window without a GPU spins its loop (see
[Window rendering](window-rendering.md)).
