# Hosting

`Pixely.Hosting` owns the entry point. A project that opts in gets a generated `Main` and writes only
what to register.

## Opting in

```xml
<PropertyGroup>
    <OutputType>Exe</OutputType>
    <PixelyHosting>true</PixelyHosting>
</PropertyGroup>
```

The property is the whole opt-in. Without it nothing is generated and the project writes its own
`Main`, as before; every other Pixely API works either way. Inside this repository a project also
references `src/Pixely.Hosting/Pixely.Hosting.csproj`, and `tutorials/Directory.Build.targets`
imports the generator, because a `ProjectReference` imports no package targets.

## What the project writes

`Program` is a `static partial class` in the project's `RootNamespace`, without an accessibility
modifier, and implements `Configure`:

```csharp
namespace Foo;

static partial class Program
{
    private static partial void Configure(PixelyAppBuilder builder, string[] args)
    {
        builder.UseDefaultContent().UseDefaultRendering(new WindowConfig(Size: (1280, 720), Title: "Game"));
        builder.AddSingleton<IRenderer<BasicRenderContext>>(TriangleRenderer.Create);
    }
}
```

- `RootNamespace` defaults to the project file name and is where `dotnet new` puts `Program`. A project
  that wants `Program` elsewhere does not opt in.
- `Configure` runs before `Build()` and receives the command-line arguments `Main` received. It is the
  only place that registers into the root container.
- Everything that used to happen after `Build()` has a registration-side form: `OnBuilt` runs after all
  services are constructed and before the provider freezes (`docs/class-registration.md`),
  `IStageManager.Load` before the first frame applies on that frame, and every registered `UiView` is
  added to its root by `Pixely.Ui` (`docs/ui.md`). Nothing needs the app object.
- `private static partial void` is the extended partial method form: the implementation is mandatory,
  so a missing `Configure` is CS8795. It needs C# 9 or later.

## Reporting failures

Pixely does not report failures on its own. A project that wants a player-visible message implements
the optional `OnException`:

```csharp
static partial void OnException(Exception exception)
{
    Console.Error.WriteLine(exception);
    MessageBox.Show(MessageBoxSeverity.Error, "Fatal error", exception.Message);
}
```

When it is implemented, a failure in `Configure`, `Build` or `Run` is passed to it and `Main` returns 1.
When it is absent, the exception propagates and the process fails as it would from a hand-written
`Main`. It runs inside the exception filter, before the application is disposed: a failure during `Run`
still has the window open behind the box, a failure during `Build` has no window yet. An exception
thrown by the handler itself is swallowed and the original failure propagates.

`static partial void` without a modifier is the removable partial form: without an implementation the
compiler removes the call. The generated file relies on that to know whether a handler exists.

## The generated file

`obj/<Configuration>/<TargetFramework>/PixelyProgram.g.cs`, shown in the IDE under
`Properties/PixelyProgram.g.cs`:

```csharp
internal static partial class Program
{
    private static partial void Configure(global::Pixely.App.PixelyAppBuilder builder, string[] args);
    static partial void OnException(global::System.Exception exception);

    private static int Main(string[] args)
    {
        try
        {
            return global::Pixely.Hosting.EntryPoint.Run(args, Configure);
        }
        catch (global::System.Exception exception) when (Handles(exception))
        {
            return 1;
        }
    }
}
```

`Pixely.Hosting.EntryPoint.Run` builds the application from `Configure` and runs it to completion; it
is public and can be called from a hand-written `Main` as well. The file is generated for `Exe` and
`WinExe` C# projects, is rewritten only when its content changes, and is removed by `dotnet clean`.

## Diagnostics

- CS8795: `Configure` is not implemented.
- CS0017: the project has another `Main`. Remove it or do not opt in.
- CS0260: the project's `Program` is not `partial`.
- CS7022: the project uses top-level statements, which take precedence over the generated `Main`.
  Do not opt in.
- An error naming `namespace <name>;` in the generated file: the project file name is not a valid
  identifier. Set `RootNamespace` in the project.
