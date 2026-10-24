# Pixely.Ecs benchmarks

Compares `Pixely.Ecs` with [Arch](https://github.com/genaray/Arch), the version pinned in `Directory.Packages.props`. Run them in Release mode without an attached debugger:

```shell
dotnet run -c Release --project benchmarks/Pixely.Ecs.Benchmarks/Pixely.Ecs.Benchmarks.csproj -- --filter '*' --inProcess
```

`--inProcess` is needed while BenchmarkDotNet 0.15.8 does not recognize .NET 11: its SDK check for a separate benchmark process fails with `GetRuntimeVersion not implemented for NotRecognized`.

Each benchmark runs at 10,000 and 100,000 entities. The worlds hold the same entities: a third each in `{Position, Velocity}`, `{Position, Velocity, Player}` and `{Position, Velocity, Enemy}`. Arch is the baseline of each ratio.

- `*IterationBenchmarks*` adds each entity's velocity to its position. Arch iterates its chunks with spans, and Pixely deconstructs the spans of a query class, the fastest direct path of each library.
- `*CreationBenchmarks*` creates entities with a position and a velocity in a new world. `Arch_CreateWithComponents` passes both components to `Create`; Pixely has no such call, so `Arch_CreateThenAdd` and `Pixely_CreateThenAdd` compare `Create` followed by two `Add` calls. `Pixely_Spawn` is the counterpart of `Arch_CreateWithComponents`: a system spawns the entities from a blueprint through a `SystemScheduler`, which applies them at its sync point. World creation is part of each operation.
- `*StructuralChangeBenchmarks*` adds a component to every entity and removes it again: the tag `Frozen`, which Pixely stores without a column while Arch stores it like any component, and `Health`, which has data in both.
- `*RandomAccessBenchmarks*` reads every entity's position by entity, in a shuffled order. Arch's `Get` checks neither the entity's version nor that it has the component, so `PixelyUnsafe` compares it with Pixely's `UnsafeGet`, which skips those checks too.
