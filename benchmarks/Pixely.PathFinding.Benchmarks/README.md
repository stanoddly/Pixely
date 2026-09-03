# Pixely.PathFinding benchmarks

Run benchmarks in Release mode without an attached debugger:

```shell
dotnet run -c Release --project benchmarks/Pixely.PathFinding.Benchmarks/Pixely.PathFinding.Benchmarks.csproj -- --filter '*LegacyComparisonBenchmarks*'
```

Useful filters:

- `*ColdStartBenchmarks*` compares first-use time and allocations before and after PR #432.
- `*ColdIndexTypeBenchmarks*` and `*ColdCostTypeBenchmarks*` compare first-use allocation footprints across numeric representations.
- `*SmallIndexTypeBenchmarks*` compares `byte`, `ushort`, `int`, and `long` indices on a 15×15 grid.
- `*PracticalIndexTypeBenchmarks*` compares `ushort`, `int`, and `long` indices on a 255×255 grid.
- `*CostTypeBenchmarks*` compares integer and floating-point cost representations on a 64×64 grid.
- `*WorkloadBenchmarks*` compares open, weighted, and unreachable searches.
- `*GridBenchmarks*` measures the `Pixely.PathFinding.Grids` layer: a warmed A* search for a size-one and a size-three agent over one clearance grid, and a clearance rebuild.
- `*LargeIndexTypeBenchmarks*` compares `int` and `long` indices on a 1024×1024 grid and requires substantially more time and memory.

Use repeated launches for the focused `ExpandTree` regression comparison:

```shell
dotnet run -c Release --project benchmarks/Pixely.PathFinding.Benchmarks/Pixely.PathFinding.Benchmarks.csproj -- --filter '*LegacyComparisonBenchmarks*ExpandTree*' --launchCount 3 --warmupCount 5 --iterationCount 12
dotnet run -c Release --project benchmarks/Pixely.PathFinding.Benchmarks/Pixely.PathFinding.Benchmarks.csproj -- --filter '*ColdStartBenchmarks*ExpandTree*' --launchCount 3 --warmupCount 5 --iterationCount 12
```

The legacy comparison measures warmed search throughput without allocations. The cold-start comparison creates a search inside each operation and measures its first-use allocations as well as execution time.

The legacy implementation in `Legacy/IndexedPathSearch.cs` is the indexed path search from commit `0201c0e`, immediately before PR #432. Its namespace and accessibility were changed to allow side-by-side measurement; its algorithm is unchanged.

Graph construction, caller-owned result lists, and `ExpandTree` output buffers are allocated during setup and excluded from warm-search measurements. `ColdStartBenchmarks` creates the search object inside each operation so that BenchmarkDotNet's allocation columns include its first-use internal storage. The cold numeric `ExpandTree` benchmarks also allocate their caller-owned output buffers inside each operation so their allocation columns represent total first-use storage. Warm benchmarks prime reusable capacity before measurement.
