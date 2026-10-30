# Fitness functions

`Pixely.Fitness` checks a game's compiled assemblies against rules. Each rule is one function that returns the members breaking it. A game runs them from its own test project.

It has no rules for the [Orange architecture](orange-architecture.md) yet.

## Running

`PixelyConventions.Evaluate(options)` returns a `FitnessReport`. `IsFit` is true when no rule has violations; `ToString()` lists every failing rule with its members. `Results` holds one `FitnessResult` per rule, and the indexer looks one up by name, e.g. `report["Pixely RenderersTakeNoBuilders"]`.

The intended shape is one test asserting `report.IsFit` with `report.ToString()` as the message, plus one test case per `Results` name so the test explorer says which rule drifted:

```csharp
private static readonly FitnessReport Report = PixelyConventions.Evaluate(new PixelyConventionsOptions(gameAssembly));

[Test]
public void Game_IsFit() => Assert.That(Report.IsFit, Is.True, Report.ToString());

[TestCaseSource(nameof(RuleNames))]
public void Rule(string name) => Assert.That(Report[name].Violations, Is.Empty, Report[name].ToString());

private static IEnumerable<string> RuleNames() => Report.Results.Select(result => result.Name);
```

Evaluate once per fixture; the functions reflect over every scanned assembly.

## Pixely conventions

`PixelyConventions.Evaluate(options)` checks how a codebase uses Pixely, whatever architecture it follows. `PixelyConventionsOptions` takes the assemblies to scan:

```csharp
FitnessReport report = PixelyConventions.Evaluate(new PixelyConventionsOptions(gameAssembly, frontendAssembly) with { FrameParticipantsAreRegistered = true });
```

- `Pixely RenderersTakeNoBuilders`: no constructor of an `IRenderer<T>` takes a `GraphicsPipelineBuilder`, `ShaderLoader`, `IShaderLoader` or `GpuMemorySystem`. `Create` builds pipelines and passes them in; a service that owns the buffers uploads geometry, so a renderer only renders.
- `Pixely VertexTypesMatchTheirElements`: an `IVertexType` struct has sequential or explicit layout, one field per `VertexElements` entry, and the entries add up to the struct's size.
- `Pixely FactoriesHideConstructors`: a type with a `public static Create` returning itself has no public constructor. Switched off by `FactoriesHideConstructors = false`; a switched-off rule stays in the report with no violations.
- `Pixely ViewsTakeOneViewModel`: every constructor of a class deriving from `UiView` takes exactly one parameter that is an `IUiViewModel`. None, several, or a collection of them is a violation. Switched off by `ViewsTakeOneViewModel = false`.
- `Pixely GpuOwnersAreDisposable`: a type with a field holding a `GraphicsPipeline`, `ComputePipeline`, `Texture`, `GpuVertexBuffer`, `GpuIndexBuffer`, `GpuStorageBuffer`, `Sampler` or `GraphicsShaderProgram`, directly or in a collection, implements `IDisposable`. A type that holds one on someone else's behalf is listed in `GpuBorrowers` with a justification; a listed type with no justification, no such field, or outside the scanned assemblies is a violation. Switched off by `GpuOwnersAreDisposable = false`.
- `Pixely FrameParticipantsAreRegistered`: every class implementing `IUpdatable`, `IRenderer<T>` or `IEventHandler<T>` is registered by a registrar, a public static `Add*` or `Use*` extension method on `PixelyAppBuilder` or `ServiceCollection`, invoked with default arguments. Off by default, `FrameParticipantsAreRegistered = true` switches it on: a delegate factory or instance registration typed by an interface and a registration outside any registrar all hide the concrete type and fail it.

## Other rule sets

`FitnessReport` and `FitnessResult` are not tied to a rule set. Any function that returns `IReadOnlyList<string>` violations can be wrapped in a `FitnessResult` and collected into a `FitnessReport`, and `FitnessReport.Merge(reportA, reportB)` folds several reports into one assertion. `TypeGraph` holds reflection helpers for writing such functions, such as `DeclaredTypes`, `SignatureTypes` and `IsPublicSurface`, and is public for that purpose.
