# Pixely.Ecs benchmarks

Compares `Pixely.Ecs` with [Arch](https://github.com/genaray/Arch), the version pinned in `Directory.Packages.props`. Run them in Release mode without an attached debugger:

```shell
dotnet run -c Release --project benchmarks/Pixely.Ecs.Benchmarks/Pixely.Ecs.Benchmarks.csproj -- --filter '*' --inProcess
```

`--inProcess` is needed while BenchmarkDotNet 0.15.8 does not recognize .NET 11: its SDK check for a separate benchmark process fails with `GetRuntimeVersion not implemented for NotRecognized`.

Each benchmark runs at 10,000 and 100,000 entities. The worlds hold the same entities: a third each in `{Position, Velocity}`, `{Position, Velocity, Player}` and `{Position, Velocity, Enemy}`. Arch is the baseline of each ratio.

A game changes a Pixely world only through systems, so the Pixely side goes through a `SystemScheduler`: a system iterates its query, and its spawners and writers record their changes, which the scheduler applies at a sync point. Where Pixely defers a change this way, Arch records it in its own `CommandBuffer` and plays it back, so both sides pay for deferral.

- `*IterationBenchmarks*` adds each entity's velocity to its position. Arch iterates its chunks with spans. Pixely updates a scheduler whose only system deconstructs the spans of a query class, so the update and its sync point, with nothing to apply, are part of the measurement.
- `*CreationBenchmarks*` creates entities with a position and a velocity in a new world. `Arch_CommandBuffer` records each entity with `Create` and two `Set` calls and plays them back. `Pixely_Spawn` spawns them in a system with a `Spawner<Position, Velocity>`, and a second system counts them after the sync point. `Pixely_Spawn_EnsureRoomFor` first asks the spawner for room for all of them, so the archetype grows once. World creation is part of each operation.
- `*StructuralChangeBenchmarks*` adds a component to every entity and removes it again: the tag `Frozen`, which Pixely stores without a column while Arch stores it like any component, and `Health`, which has data in both. Pixely's system adds with a `ComponentAdder<T>` in one update and removes with a `ComponentRemover<T>` in the next. Arch records the adds in its `CommandBuffer` and plays them back, then does the same with the removes.
- `*RandomAccessBenchmarks*` reads every entity's position by entity, in a shuffled order. `Pixely` reads through the `ReadOnlyComponentLookup<Position>` that `AddReadOnlyComponentLookup` registers for code outside systems, the checked path. Arch's `Get` checks neither the entity's version nor that it has the component, so `PixelyUnsafe` compares it with that lookup's `UnsafeGet`, which skips those checks too. `PixelyLookup` reads through a system's `ReadOnlyComponentLookup<Position>`. `PixelyTwoLookups` and `PixelyPairLookup` also read each velocity, through two single lookups and through one `ReadOnlyComponentLookup<Position, Velocity>`.

The growth benchmark is not a BenchmarkDotNet benchmark, because BenchmarkDotNet reports means, which hide the frame that copies a large column or waits for a collection:

```shell
dotnet run -c Release --project benchmarks/Pixely.Ecs.Benchmarks/Pixely.Ecs.Benchmarks.csproj -- growth [entities per frame]
```

It grows one archetype to 1,000,000 entities, 2,000 per frame by default, rounded up to whole frames, with 1 to 1,000,000 entities per frame: with a system's `Spawn`, without and with `EnsureRoomFor` for all of them in the first frame, and with Arch's `CommandBuffer`, played back each frame. It reports the total time, the slowest and the 99th percentile frame, the collections of each generation, the GC pause and the allocated memory, as the median of 7 runs. Every case is warmed up before any is measured, and the runs of the cases are interleaved, because the first measurements of a process run slower.
