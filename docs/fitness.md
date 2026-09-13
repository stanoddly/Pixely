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

- `ExtraGameNamespaces` lists namespaces the game adds to `Foo.Game` beyond the ones the document names, each with a justification. Rule 08 reports a type outside the documented and listed namespaces, a listed namespace without a justification, and a listed namespace that holds no types.
- `MechanicsHaveNoPublicConstructors`, `MechanicsTakeStateRootThroughConstructor` and `GameGrantsNoInternalAccess` are `init` properties that switch off hardened SHOULD rules. They default to true.

From the prefix, rule 00 derives the rest and reports what it could not derive:

- The assemblies, loaded by name: `Foo.Game`, `Foo.Frontend`, `Foo.Frontend.Rendering`, `Foo.Frontend.Audio`, `Foo.Ai`, `Foo.Scenario`, and `Foo.Executable` or `Foo`. The test project must reference the executable project so they sit in its output directory. The optional four are absent when the game has no such project; a missing `Game`, `Frontend` or executable stops the evaluation at rule 00.
- The state root: the one class in `Foo.Game.State` no other `State` type holds in a field.
- The two containers: every registrar in the production assemblies, a public static `Add*` extension method on `PixelyAppBuilder` or `ServiceCollection` in the project's root namespace, is invoked with default arguments on one `PixelyAppBuilder` and one `ServiceCollection`. A registrar that throws on defaults is a violation.
- The repository root: the directory above `src/Foo.Game/Foo.Game.csproj`, found by walking up from the test output directory. With it, rule 01 checks the `ProjectReference` items of each project and rule 00 compares the `src/Foo.*` directories against the loaded assemblies. Without it, e.g. when the tests run from a package, both checks are skipped.

## Running

`PeachArchitecture.Evaluate(options)` returns a `FitnessReport`. `IsFit` is true when no rule has violations; `ToString()` lists every failing rule with its members. `Results` holds one `FitnessResult` per rule, and the indexer looks one up by name, e.g. `report["17 StateHasNoPublicSetters"]`.

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

## Other rule sets

`FitnessReport` and `FitnessResult` are not tied to the Peach rules. Any function that returns `IReadOnlyList<string>` violations can be wrapped in a `FitnessResult` and collected into a `FitnessReport`, and `FitnessReport.Merge(reportA, reportB)` folds several reports into one assertion. `TypeGraph` holds the reflection helpers the Peach functions use, such as `DeclaredTypes`, `SignatureTypes` and `IsPublicSurface`, and is public for that purpose.
