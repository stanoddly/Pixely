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
`Main`; every other Pixely API works either way.

## What the project writes

`Program` is a `static partial class` in the project's `RootNamespace` and declares `Configure`:

```csharp
namespace Foo;

static partial class Program
{
    static void Configure(PixelyAppBuilder builder, string[] args)
    {
        builder.UseDefaultContent().UseDefaultRendering(new WindowConfig(Size: (1280, 720), Title: "Game"));
        builder.AddSingleton<IRenderer<BasicRenderContext>>(TriangleRenderer.Create);
    }
}
```

- `RootNamespace` defaults to the project file name and is where `dotnet new` puts `Program`. A project
  that wants `Program` elsewhere does not opt in.
- `Configure` runs before `Build()` and receives the command-line arguments `Main` received. It is the
  only place that registers into the root container, and nothing after it sees the app object: work
  that needs built services runs in `OnBuilt` (`docs/class-registration.md`), a stage is loaded
  through `IStageManager.Load`, and a registered `UiView` is added to its root by `Pixely.Ui`
  (`docs/ui.md`).
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
It adds `Main` to `Program`, which calls `Pixely.Hosting.EntryPoint.Run` with `Configure` and
`OnException`, and `PixelyProgramDefaults` to the namespace, which holds the default `OnException`.
Nothing else is added. The generated part of `Program` states no accessibility, so the project's part
may state any. The file is generated for `Exe` and `WinExe` C# projects, is rewritten only when its
content changes, and is removed by `dotnet clean`.

`EntryPoint.Run` is public and can be called from a hand-written `Main` as well, with or without a
handler.

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
