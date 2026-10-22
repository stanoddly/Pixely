# Pixely.Ecs benchmarks

Compares `Pixely.Ecs` with [Arch](https://github.com/genaray/Arch), the version pinned in `Directory.Packages.props`. Run them in Release mode without an attached debugger:

```shell
dotnet run -c Release --project benchmarks/Pixely.Ecs.Benchmarks/Pixely.Ecs.Benchmarks.csproj -- --filter '*' --inProcess
```

`--inProcess` is needed while BenchmarkDotNet 0.15.8 does not recognize .NET 11: its SDK check for a separate benchmark process fails with `GetRuntimeVersion not implemented for NotRecognized`.

Each benchmark runs at 10,000 and 100,000 entities. The worlds hold the same entities: a third each in `{Position, Velocity}`, `{Position, Velocity, Player}` and `{Position, Velocity, Enemy}`. Arch is the baseline of each ratio.

- `*IterationBenchmarks*` adds each entity's velocity to its position. Arch iterates its chunks with spans and Pixely uses a `[Query]` struct, the fastest direct path of each library.
- `*CreationBenchmarks*` creates entities with a position and a velocity in a new world. `Arch_CreateWithComponents` passes both components to `Create`; Pixely has no such call, so `Arch_CreateThenAdd` and `Pixely_CreateThenAdd` compare `Create` followed by two `Add` calls. World creation is part of each operation.
- `*StructuralChangeBenchmarks*` adds a tag component to every entity and removes it again.
- `*RandomAccessBenchmarks*` reads every entity's position by entity, in a shuffled order.
