# Fitness functions

`Pixely.Fitness` checks a game's compiled assemblies against rules. Each rule is one function that returns the members breaking it. A game runs them from its own test project.

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
- `Pixely FrameParticipantsAreRegistered`: every class implementing `IUpdatable` or `IRenderer<T>` is registered by a registrar, a public static `Add*` or `Use*` extension method on `PixelyAppBuilder` or `ServiceCollection`, invoked with default arguments. Off by default, `FrameParticipantsAreRegistered = true` switches it on: a delegate factory or instance registration typed by an interface and a registration outside any registrar all hide the concrete type and fail it. An Orange game leaves it off, because its `AddGame` is internal.

## Peach architecture

`PeachArchitecture.Evaluate(options)` checks the rules of the [Peach architecture](peach-architecture.md). The document fixes the names of everything, so the prefix is enough:

```csharp
PeachArchitectureOptions options = new PeachArchitectureOptions("Foo")
{
    ExtraGameNamespaces = [new ExtraNamespace("Foo.Game.Persistence", "Saving to disk is neither State nor a Mechanic")]
};
```

- `ExtraGameNamespaces` lists namespaces the game adds to `Foo.Game` beyond the ones the document names, each with a justification.
- `MechanicsTakeStateRootThroughConstructor` is an `init` property that switches off the hardened SHOULD rule. It defaults to true.
- `RepositoryRoot` is an `init` property for a game whose test output sits outside its repository. It is the directory holding `src/Foo.Game/Foo.Game.csproj`.

From the prefix, `GameResolvesFromPrefix` derives the rest and reports what it could not derive:

- The assemblies, loaded by name: `Foo.Game`, `Foo.Frontend`, `Foo.Frontend.Rendering`, `Foo.Frontend.Audio`, `Foo.Ai`, `Foo.Scenario`, and `Foo.Executable` or `Foo`. The test project must reference the executable project so they sit in its output directory. The optional four are absent when the game has no such project; a missing `Game`, `Frontend` or executable stops the evaluation at `GameResolvesFromPrefix`.
- The state root: the one class in `Foo.Game.State` no other `State` type holds in a field.
- The two containers: every registrar in the production assemblies, a public static `Add*` extension method on `PixelyAppBuilder` or `ServiceCollection` in the project's root namespace, is invoked with default arguments on one `PixelyAppBuilder` and one `ServiceCollection`. A registrar that throws on defaults is a violation.
- The repository root: the directory above `src/Foo.Game/Foo.Game.csproj`, found by walking up from the test output directory unless `RepositoryRoot` gives it. With it, `ProjectReferencesMatchTheGraph` checks the `ProjectReference` items of each project and `GameResolvesFromPrefix` reports a loaded part with no `src/Foo.{Part}` directory. Each part references exactly its arrows, except the executable: it references at least its arrows and may reference other projects, e.g. a game-agnostic library under `src/`. Without it, e.g. when the tests run from a package, both checks are skipped. A root given by `RepositoryRoot` without `src/Foo.Game/Foo.Game.csproj` is a violation of `GameResolvesFromPrefix`. Both checks are then skipped.

A Peach game takes the assemblies for the Pixely conventions from its resolved options:

```csharp
FitnessReport report = FitnessReport.Merge(PeachArchitecture.Evaluate(options), PixelyConventions.Evaluate(PixelyConventionsOptions.ForPeach(options) with { FrameParticipantsAreRegistered = true }));
```

## Orange architecture

`OrangeArchitecture.Evaluate(options)` checks the rules of the [Orange architecture](orange-architecture.md) that constructors and types show. No rule checks projects or namespaces, other than `Foo.Vocabulary`. `OrangeArchitectureOptions` takes the game's name, which names `Foo.Vocabulary`, and the assemblies to scan. Merge the report with the Pixely conventions for one assertion:

```csharp
FitnessReport report = FitnessReport.Merge(PixelyConventions.Evaluate(new PixelyConventionsOptions(gameAssembly)), OrangeArchitecture.Evaluate(new OrangeArchitectureOptions("Ashfall", gameAssembly)));
```

- `Orange ViewsReadOnlyTheirViewModel`: no constructor of a `UiView` takes a query, a lookup, a unique, an adder, a remover, the destroyer, a spawner, `World`, a system, or a message reader or writer, also inside another type such as `Func<MessageReader<T>>`.
- `Orange RenderersReadNoMessages`: no constructor of an `IRenderer<T>` takes a `MessageReader<T>`, a `ParticipantMessageReader<,>` or `Messages<T>`. A renderer that is also an updatable fails too, because the rule cannot tell which method reads. A renderer may take a `MessageWriter<T>`.
- `Orange OutsideSystemsTakeNoWritingQueries`: no constructor of an `IUpdatable` or `IRenderer<T>` that is not a system takes a query with a `Span<T>` or `Optional<Span<T>>`. `AddQuery` throws for one too, but only when the provider is built. A helper is a static function, so it has no constructor to check.
- `Orange OutsideSystemsTakeNoWritableUniques`: no constructor of an `IUpdatable` or `IRenderer<T>` that is not a system takes a `Unique<T>`, and none of these or of a `UiView` takes a class that a system takes as `Unique<T>`, also inside another type such as `Func<T>`. `AddUnique` registers neither, so this finds a game that also registers the class as a service. A view taking `Unique<T>` fails `Orange ViewsReadOnlyTheirViewModel` instead. Nothing checks that the read-only type exposes no means to write.
- `Orange ViewModelsHaveOneWriter`: no view model is taken by the constructors of two systems or updatables, counting a parameter typed by an interface or base class of the view model, except `object`. An updatable counts because it writes a view model of state outside the world. A view model that none of them takes passes.
- `Orange VocabularyHoldsOnlyPrimitives`: `Foo.Vocabulary` holds only enums, read-only record structs, and static classes whose only members are `const` fields and `static readonly` fields of a value type or `string`.
- `Orange ComponentsHoldNoReferences`: no component holds a reference other than a `string`, also inside a struct it holds. A component is any struct that a query, spawner, filter, lookup, adder or remover names in the scanned assemblies.

## Other rule sets

`FitnessReport` and `FitnessResult` are not tied to a rule set. Any function that returns `IReadOnlyList<string>` violations can be wrapped in a `FitnessResult` and collected into a `FitnessReport`, and `FitnessReport.Merge(reportA, reportB)` folds several reports into one assertion. `TypeGraph` holds reflection helpers for writing such functions, such as `DeclaredTypes`, `SignatureTypes` and `IsPublicSurface`, and is public for that purpose.
