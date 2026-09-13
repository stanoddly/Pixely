# Fitness functions

`Pixely.Fitness` checks a game's compiled assemblies against the rules in [peach-architecture.md](peach-architecture.md). Each rule is one function that returns the members breaking it. A game runs them from its own test project; Pixely ships the functions, the game supplies what only it knows.

## Options

`PeachArchitectureOptions` names the parts of the game:

```csharp
PeachArchitectureOptions options = new PeachArchitectureOptions(
    GamePrefix: "Foo",
    Game: typeof(GameRegistrar).Assembly,
    Frontend: typeof(FrontendRegistrar).Assembly,
    Rendering: typeof(RenderingRegistrar).Assembly,
    Audio: null,
    Ai: null,
    Scenario: null,
    Executable: Assembly.Load("Foo"),
    StateRoot: typeof(GameState),
    ExtraGameNamespaces: ["Foo.Game.Persistence"],
    ComposeRoot: builder => { builder.AddGamePersistence(); builder.AddFrontendRoot(); },
    ComposeStage: services => { services.AddGame(); services.AddFrontendRendering(); services.AddFrontend(); })
{
    RepositoryRoot = "/path/to/repo"
};
```

- `Rendering`, `Audio`, `Ai` and `Scenario` are null when the game has no such project.
- `ExtraGameNamespaces` lists namespaces the game adds to `Foo.Game` beyond the ones the document names.
- `ComposeRoot` and `ComposeStage` register exactly what `Foo.Executable` registers, so the container rules see the real composition without building a provider.
- `RepositoryRoot` enables rule 01, which reads the `.csproj` files. Leave it null when the tests cannot see the source tree, e.g. when they run from a package.
- `MechanicsHaveNoPublicConstructors`, `MechanicsTakeStateRootThroughConstructor` and `GameGrantsNoInternalAccess` switch off hardened SHOULD rules. They default to true.

## Running

`PeachArchitecture.Evaluate(options)` returns a `FitnessReport`. `IsFit` is true when no rule has violations; `ToString()` lists every failing rule with its members. `Results` holds one `FitnessResult` per rule, and the indexer looks one up by name, e.g. `report["17 StateHasNoPublicSetters"]`.

The intended shape is one test asserting `report.IsFit` with `report.ToString()` as the message, plus one test case per `Results` name so the test explorer says which rule drifted:

```csharp
private static readonly FitnessReport Report = PeachArchitecture.Evaluate(Options);

[Test]
public void Game_IsFit() => Assert.That(Report.IsFit, Is.True, Report.ToString());

[TestCaseSource(nameof(RuleNames))]
public void Rule(string name) => Assert.That(Report[name].Violations, Is.Empty, Report[name].ToString());

private static IEnumerable<string> RuleNames() => Report.Results.Select(result => result.Name);
```

Evaluate once per fixture; the functions reflect over every production assembly.

## Other rule sets

`FitnessReport` and `FitnessResult` are not tied to the Peach rules. Any function that returns `IReadOnlyList<string>` violations can be wrapped in a `FitnessResult` and collected into a `FitnessReport`, and `FitnessReport.Merge(reportA, reportB)` folds several reports into one assertion. `TypeGraph` holds the reflection helpers the Peach functions use, such as `DeclaredTypes`, `SignatureTypes` and `IsPublicSurface`, and is public for that purpose.
