# Fitness functions

`Pixely.Fitness` checks a game's compiled assemblies against the rules in [peach-architecture.md](peach-architecture.md). Each rule is one function that returns the members breaking it. A game runs them from its own test project.

## Options

The document fixes the names of everything, so the prefix is enough:

```csharp
PeachArchitectureOptions options = new PeachArchitectureOptions("Foo")
{
    ExtraGameNamespaces = [new ExtraNamespace("Foo.Game.Persistence", "Saving to disk is neither State nor a Mechanic")]
};
```

- `ExtraGameNamespaces` lists namespaces the game adds to `Foo.Game` beyond the ones the document names, each with a justification.
- `MechanicsTakeStateRootThroughConstructor` is an `init` property that switches off the hardened SHOULD rule. It defaults to true.

From the prefix, `GameResolvesFromPrefix` derives the rest and reports what it could not derive:

- The assemblies, loaded by name: `Foo.Game`, `Foo.Frontend`, `Foo.Frontend.Rendering`, `Foo.Frontend.Audio`, `Foo.Ai`, `Foo.Scenario`, and `Foo.Executable` or `Foo`. The test project must reference the executable project so they sit in its output directory. The optional four are absent when the game has no such project; a missing `Game`, `Frontend` or executable stops the evaluation at `GameResolvesFromPrefix`.
- The state root: the one class in `Foo.Game.State` no other `State` type holds in a field.
- The two containers: every registrar in the production assemblies, a public static `Add*` extension method on `PixelyAppBuilder` or `ServiceCollection` in the project's root namespace, is invoked with default arguments on one `PixelyAppBuilder` and one `ServiceCollection`. A registrar that throws on defaults is a violation.
- The repository root: the directory above `src/Foo.Game/Foo.Game.csproj`, found by walking up from the test output directory. With it, `ProjectReferencesMatchTheGraph` checks the `ProjectReference` items of each project and `GameResolvesFromPrefix` reports a loaded part with no `src/Foo.{Part}` directory. Without it, e.g. when the tests run from a package, both checks are skipped.

## Running

`PeachArchitecture.Evaluate(options)` returns a `FitnessReport`. `IsFit` is true when no rule has violations; `ToString()` lists every failing rule with its members. `Results` holds one `FitnessResult` per rule, and the indexer looks one up by name, e.g. `report["StateHasNoPublicSetters"]`.

The intended shape is one test asserting `report.IsFit` with `report.ToString()` as the message, plus one test case per `Results` name so the test explorer says which rule drifted:

```csharp
private static readonly FitnessReport Report = PeachArchitecture.Evaluate(new PeachArchitectureOptions("Foo"));

[Test]
public void Game_IsFit() => Assert.That(Report.IsFit, Is.True, Report.ToString());

[TestCaseSource(nameof(RuleNames))]
public void Rule(string name) => Assert.That(Report[name].Violations, Is.Empty, Report[name].ToString());

private static IEnumerable<string> RuleNames() => Report.Results.Select(result => result.Name);
```

Evaluate once per fixture; the functions reflect over every production assembly.

## Pixely conventions

`PixelyConventions.Evaluate(options)` checks how a codebase uses Pixely, whatever architecture it follows. `PixelyConventionsOptions` takes the assemblies to scan; a Peach game takes them from its resolved options:

```csharp
FitnessReport report = FitnessReport.Merge(
    PeachArchitecture.Evaluate(peach),
    PixelyConventions.Evaluate(PixelyConventionsOptions.ForPeach(peach) with { FrameParticipantsAreRegistered = true }));
```

- `Pixely RenderersTakeNoBuilders`: no constructor of an `IRenderer<T>` takes a `GraphicsPipelineBuilder`, `ShaderLoader`, `IShaderLoader` or `GpuMemorySystem`. `Create` builds pipelines and passes them in; a service that owns the buffers uploads geometry, so a renderer only renders.
- `Pixely VertexTypesMatchTheirElements`: an `IVertexType` struct has sequential or explicit layout, one field per `VertexElements` entry, and the entries add up to the struct's size.
- `Pixely FactoriesHideConstructors`: a type with a `public static Create` returning itself has no public constructor. Switched off by `FactoriesHideConstructors = false`; a switched-off rule stays in the report with no violations.
- `Pixely ViewsTakeOneViewModel`: every constructor of a class deriving from `UiView` takes exactly one parameter that is an `IUiViewModel`. None, several, or a collection of them is a violation. Switched off by `ViewsTakeOneViewModel = false`.
- `Pixely GpuOwnersAreDisposable`: a type with a field holding a `GraphicsPipeline`, `ComputePipeline`, `Texture`, `GpuVertexBuffer`, `GpuIndexBuffer`, `GpuStorageBuffer`, `Sampler` or `GraphicsShaderProgram`, directly or in a collection, implements `IDisposable`. A type that holds one on someone else's behalf is listed in `GpuBorrowers` with a justification; a listed type with no justification, no such field, or outside the scanned assemblies is a violation. Switched off by `GpuOwnersAreDisposable = false`.
- `Pixely FrameParticipantsAreRegistered`: every class implementing `IUpdatable`, `IRenderer<T>` or `IEventHandler<T>` is registered by a registrar, a public static `Add*` or `Use*` extension method on `PixelyAppBuilder` or `ServiceCollection`, invoked with default arguments. Off by default, `FrameParticipantsAreRegistered = true` switches it on: a delegate factory or instance registration typed by an interface and a registration outside any registrar all hide the concrete type and fail it.

## Other rule sets

`FitnessReport` and `FitnessResult` are not tied to either rule set. Any function that returns `IReadOnlyList<string>` violations can be wrapped in a `FitnessResult` and collected into a `FitnessReport`, and `FitnessReport.Merge(reportA, reportB)` folds several reports into one assertion. `TypeGraph` holds the reflection helpers the Peach functions use, such as `DeclaredTypes`, `SignatureTypes` and `IsPublicSurface`, and is public for that purpose.
