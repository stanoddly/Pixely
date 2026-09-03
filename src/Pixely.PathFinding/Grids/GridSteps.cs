using System.Runtime.CompilerServices;

namespace Pixely.PathFinding.Grids;

/// <summary>
/// One legal step to a neighbouring anchor, with the geometric distance travelled.
/// </summary>
/// <remarks>
/// The distance is not a cost. A consumer with per-tile weighting multiplies it by the weight it charges for the step;
/// a consumer with uniform terrain picks a cost per step class instead, which is what <see cref="UniformGridGraph{TIndex, TCost, TOverlay}"/> does.
/// </remarks>
public readonly record struct GridStep(int Index, float Distance)
{
    /// <summary>The distance of a step to a cardinal neighbour.</summary>
    public const float CardinalDistance = 1f;

    /// <summary>The distance of a step to a diagonal neighbour.</summary>
    public static float DiagonalDistance { get; } = MathF.Sqrt(2f);
}

/// <summary>
/// Blocks anchors on top of the static clearance for the duration of one query.
/// </summary>
/// <remarks>
/// An overlay is asked about an <em>anchor</em>, not about a tile. An agent of size N anchored at an index occupies an NxN footprint,
/// so a consumer that wants to block a single tile for such an agent must report every anchor whose footprint covers that tile.
/// That expansion is therefore specific to one agent size: an overlay built for size one is wrong for size two. A consumer serving
/// several sizes either builds one <see cref="GridSteps{TOverlay}"/> per size over a size-appropriate overlay, or writes an overlay
/// that scans the footprint itself, which is the slower but size-agnostic option.
/// An overlay can only make an anchor less walkable than its clearance says, never more; a query that needs to ignore an existing
/// blocker rebuilds a scratch <see cref="ClearanceGrid"/> from amended flags instead.
/// <para>Implementations are struct-constrained wherever they are consumed so the check inlines and no interface dispatch enters the expansion path.</para>
/// </remarks>
public interface IGridOverlay
{
    bool IsBlocked(int index);
}

/// <summary>
/// An overlay that blocks nothing, for queries with no per-query exceptions.
/// </summary>
public readonly struct NoGridOverlay : IGridOverlay
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsBlocked(int index)
    {
        return false;
    }
}

/// <summary>
/// Enumerates the legal steps a square agent of a given size can take from an anchor.
/// </summary>
/// <remarks>
/// The agent size is an argument rather than a field, so one instance serves agents of every size over the same clearance grid.
/// The corner rule of <see cref="GridConnectivity.EightWayNoCornerCutting"/> is evaluated at the agent's size against clearance and the overlay,
/// not against a single tile, so a diagonal step requires both of its cardinal steps to fit for that agent.
/// <para>The origin anchor itself is not validated; enumeration only decides which destinations are legal.</para>
/// </remarks>
/// <typeparam name="TOverlay">A value-type overlay blocking anchors on top of the static clearance.</typeparam>
public readonly struct GridSteps<TOverlay> where TOverlay : struct, IGridOverlay
{
    private static ReadOnlySpan<sbyte> CardinalDeltas => [1, 0, -1, 0, 0, 1, 0, -1];
    private static ReadOnlySpan<sbyte> DiagonalDeltas => [1, 1, 1, -1, -1, 1, -1, -1];

    private readonly ClearanceGrid _clearance;
    private readonly TOverlay _overlay;

    public GridSteps(ClearanceGrid clearance, GridConnectivity connectivity, TOverlay overlay)
    {
        ArgumentNullException.ThrowIfNull(clearance);
        if (!Enum.IsDefined(connectivity))
        {
            throw new ArgumentOutOfRangeException(nameof(connectivity), connectivity, "The connectivity is not a defined value.");
        }

        _clearance = clearance;
        _overlay = overlay;
        Connectivity = connectivity;
    }

    public GridGeometry Geometry => _clearance.Geometry;
    public GridConnectivity Connectivity { get; }

    /// <summary>The largest number of steps <see cref="Enumerate"/> can write.</summary>
    public int MaximumDegree => Connectivity == GridConnectivity.FourWay ? 4 : 8;

    /// <summary>
    /// Writes every legal step from the anchor for an agent of the given size and returns how many were written.
    /// </summary>
    /// <param name="originIndex">The anchor the agent steps from.</param>
    /// <param name="agentSize">The agent's side length, from one through <see cref="ClearanceGrid.MaximumAgentSize"/>.</param>
    /// <param name="steps">A buffer of at least <see cref="MaximumDegree"/> entries, overwritten from the start.</param>
    public int Enumerate(int originIndex, int agentSize, Span<GridStep> steps)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(agentSize, 1, nameof(agentSize));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(agentSize, ClearanceGrid.MaximumAgentSize, nameof(agentSize));
        if (steps.Length < MaximumDegree)
        {
            throw new ArgumentException("The step buffer must hold as many steps as the maximum degree.", nameof(steps));
        }

        (int x, int y) = Geometry.GetPosition(originIndex);
        int count = 0;
        for (int delta = 0; delta < CardinalDeltas.Length; delta += 2)
        {
            if (TryOccupy(x + CardinalDeltas[delta], y + CardinalDeltas[delta + 1], agentSize, out int index))
            {
                steps[count++] = new GridStep(index, GridStep.CardinalDistance);
            }
        }

        if (Connectivity == GridConnectivity.FourWay)
        {
            return count;
        }

        bool cutsCorners = Connectivity == GridConnectivity.EightWay;
        for (int delta = 0; delta < DiagonalDeltas.Length; delta += 2)
        {
            int stepX = DiagonalDeltas[delta];
            int stepY = DiagonalDeltas[delta + 1];
            if (!TryOccupy(x + stepX, y + stepY, agentSize, out int index))
            {
                continue;
            }

            if (!cutsCorners && (!TryOccupy(x + stepX, y, agentSize, out _) || !TryOccupy(x, y + stepY, agentSize, out _)))
            {
                continue;
            }

            steps[count++] = new GridStep(index, GridStep.DiagonalDistance);
        }

        return count;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryOccupy(int x, int y, int agentSize, out int index)
    {
        return Geometry.TryGetIndex(x, y, out index) && _clearance.GetClearance(index) >= agentSize && !_overlay.IsBlocked(index);
    }
}
