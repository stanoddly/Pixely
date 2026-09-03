# Path finding on grids

`Pixely.PathFinding.Grids` is a grid layer over the graph-level search in `Pixely.PathFinding`. It supplies the pieces every grid consumer would otherwise rebuild by hand — index mapping, the neighbour fan, the corner-cutting rule, an admissible heuristic — and answers "does an agent of size N fit here" for square agents of any size from a single precomputed grid.

It changes nothing about `IndexedPathSearch<TIndex, TCost>`. A footprint is a property of the graph, not of the search.

## Layout

```
GridGeometry      index <-> (x, y), row-major, zero-based
ClearanceGrid     largest free square anchored at each tile, one byte per tile
GridSteps<T>      legal steps from an anchor for an agent size, with the corner rule
GridHeuristic<,>  admissible estimate from a lower bound per step class
UniformGridGraph  the ready-made IIndexedPathGraph for unweighted terrain
```

The primitives are meant to be composed. `UniformGridGraph` is one way to assemble them; a consumer with per-tile cost writes its own readonly struct over the same primitives.

## Minimal use

```csharp
GridGeometry geometry = new GridGeometry(32, 32);
ClearanceGrid clearance = new ClearanceGrid(geometry);
clearance.Rebuild(blockedFlags);

GridSteps<NoGridOverlay> steps = new GridSteps<NoGridOverlay>(clearance, GridConnectivity.EightWayNoCornerCutting, default);
UniformGridGraph<int, float, NoGridOverlay> graph = new UniformGridGraph<int, float, NoGridOverlay>(steps, agentSize: 2, cardinalCost: 1f, diagonalCost: MathF.Sqrt(2f));

IndexedPathSearch<int, float> search = new IndexedPathSearch<int, float>();
search.FindPath(graph, geometry.GetIndex(0, 0), geometry.GetIndex(31, 31), path, graph.GetHeuristic());
```

`GetHeuristic()` returns the heuristic matching the graph's costs and connectivity, so the two cannot drift apart. A four-way consumer uses the single-cost constructor, `new UniformGridGraph<int, float, NoGridOverlay>(steps, agentSize, cardinalCost)`, which rejects any other connectivity.

## Anchors and footprints

Clearance is anchored at the **minimum corner**: the clearance at `(x, y)` measures the free square extending toward increasing x and y. An agent of size N anchored at `(x, y)` occupies `(x, y)` through `(x + N - 1, y + N - 1)`.

A path is therefore a path of anchors, not of centres. A consumer that draws the agent centred converts by half a footprint on its own side; the layer has no opinion on coordinate origin or world units.

## One grid, many agent sizes

`ClearanceGrid` stores the largest free square, not a per-size verdict, so a single grid and a single rebuild serve every agent size at once:

```csharp
ClearanceGrid clearance = new ClearanceGrid(geometry);   // rebuilt when topology changes
GridSteps<NoGridOverlay> steps = new GridSteps<NoGridOverlay>(clearance, connectivity, default);

UniformGridGraph<int, float, NoGridOverlay> scout = new UniformGridGraph<int, float, NoGridOverlay>(steps, 1, 1f, diagonal);
UniformGridGraph<int, float, NoGridOverlay> hauler = new UniformGridGraph<int, float, NoGridOverlay>(steps, 3, 1f, diagonal);
```

`ClearanceGrid.Fits` and `GridSteps.Enumerate` take the size as an argument. The heuristic is independent of size. Only the graph struct is per-size, because the search specializes on it, and constructing one costs nothing.

Sizes run from 1 through `ClearanceGrid.MaximumAgentSize` (255); clearances saturate there. A larger size is rejected rather than answered wrongly.

## Connectivity and the corner rule

| Value | Fan | Maximum degree |
| --- | --- | --- |
| `FourWay` | four cardinals | 4 |
| `EightWay` | cardinals and diagonals | 8 |
| `EightWayNoCornerCutting` | cardinals, and a diagonal only when both of its cardinals are legal | 8 |

The corner rule is evaluated at the agent's size against clearance and the overlay, not against a single tile, so a 2x2 agent may not squeeze diagonally between two blocked regions.

`GridStep.Distance` is geometric distance — 1 for a cardinal step, `sqrt(2)` for a diagonal — and not a cost. A consumer with terrain weighting multiplies it by whatever it charges.

## Per-query exceptions

Clearance answers the **static** question. Per-query exceptions stay outside it, and there are two shapes:

- **Adding a blocker** for one query — layer it with an `IGridOverlay`. `NoGridOverlay` is the zero-cost default, and the struct constraint keeps the check inlined.
- **Ignoring an existing blocker** for one query — rebuild a scratch `ClearanceGrid` from amended flags. Layering cannot make an anchor *more* walkable than its clearance says.

An overlay is asked about an **anchor**, not a tile. An agent of size N anchored at an index covers an NxN footprint, so a consumer that wants to block one tile for such an agent reports every anchor whose footprint covers it. The overlay participates in every fit check the enumeration makes — the destination and both intermediate cardinals of a diagonal — because filtering emitted destinations afterwards would let an overlay-blocked corner through.

That anchor expansion is specific to one agent size. Unlike the clearance grid, an overlay built for a size-one agent is wrong for a size-two one, so a consumer serving several sizes either builds one `GridSteps` per size over a size-appropriate overlay, or writes an overlay that scans the footprint itself — slower, but size-agnostic. The clearance grid is still shared in both cases.

Nothing here reserves space between agents. Agents that must avoid each other express that through an overlay per query.

## Heuristic

`GridHeuristic<TIndex, TCost>` is built from a lower bound per step class:

```
diagonalSteps * min(diagonalCost, 2 * cardinalCost) + straightSteps * min(cardinalCost, diagonalCost)
```

This is admissible for any non-negative pair, so no ratio needs validating: a diagonal displacement can always be walked as two cardinals, and a straight displacement can always be zig-zagged with diagonals. It degenerates to octile distance at `diagonalCost = sqrt(2) * cardinalCost` and to Chebyshev at `diagonalCost = cardinalCost`. The single-cost constructor is the four-way form and yields Manhattan distance.

No `sqrt(2)` is baked in, so an integral `TCost` needs no rounding rule. Products saturate at `TCost.MaxValue`, which `IndexedPathSearch` already treats as the lowest priority.

Both costs must be lower bounds. A consumer whose per-tile cost varies passes the cheapest step it can ever charge.

## Cost and allocation

`Rebuild` is one pass over the grid and reuses its buffer. `Enumerate` writes into a caller-owned `Span`. Every type parameter that enters the expansion path is struct-constrained, so the JIT specializes the search and inlines the constrained calls; no delegate or interface dispatch is involved.

`GridBenchmarks` in `benchmarks/Pixely.PathFinding.Benchmarks` measures a warmed search and a rebuild with `MemoryDiagnoser`, and both report zero allocations.

## Writing a weighted graph

A consumer with per-tile cost keeps the primitives and replaces only the adapter:

```csharp
internal readonly struct TerrainGraph : IIndexedPathGraph<int, float>
{
    private readonly GridSteps<WetnessOverlay> _steps;
    private readonly float[] _tileCosts;
    private readonly int _agentSize;

    public int NodeCount => _steps.Geometry.NodeCount;
    public int MaximumDegree => _steps.MaximumDegree;

    public int GetEdges(int origin, Span<PathEdge<int, float>> edges)
    {
        Span<GridStep> steps = stackalloc GridStep[_steps.MaximumDegree];
        int count = _steps.Enumerate(origin, _agentSize, steps);
        for (int index = 0; index < count; index++)
        {
            edges[index] = new PathEdge<int, float>(steps[index].Index, steps[index].Distance * _tileCosts[steps[index].Index]);
        }

        return count;
    }
}
```

Its heuristic then uses the cheapest tile cost as the lower bound, not the nominal one.
