using System.Numerics;

namespace Pixely.PathFinding.Grids;

/// <summary>
/// Presents a grid as an indexed path graph with one cost per step class and a fixed agent size.
/// </summary>
/// <remarks>
/// This is the ready-made adapter for a consumer whose terrain is unweighted. Anything with per-tile or dynamic cost writes its own
/// readonly struct over <see cref="GridSteps{TOverlay}"/>, multiplying <see cref="GridStep.Distance"/> by whatever it charges for the step.
/// <para>
/// The agent size is fixed per graph so the search specializes on it. Agents of different sizes share one <see cref="ClearanceGrid"/> and
/// one <see cref="GridHeuristic{TIndex, TCost}"/>; only this struct is constructed per size, which costs nothing.
/// </para>
/// </remarks>
/// <typeparam name="TOverlay">A value-type overlay blocking anchors on top of the static clearance, or <see cref="NoGridOverlay"/>.</typeparam>
public readonly struct UniformGridGraph<TIndex, TCost, TOverlay> : IIndexedPathGraph<TIndex, TCost>
    where TIndex : unmanaged, IBinaryInteger<TIndex>, IMinMaxValue<TIndex>
    where TCost : unmanaged, INumber<TCost>, IMinMaxValue<TCost>
    where TOverlay : struct, IGridOverlay
{
    private readonly GridSteps<TOverlay> _steps;
    private readonly int _agentSize;
    private readonly TCost _cardinalCost;
    private readonly TCost _diagonalCost;

    public UniformGridGraph(GridSteps<TOverlay> steps, int agentSize, TCost cardinalCost, TCost diagonalCost)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(agentSize, 1, nameof(agentSize));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(agentSize, ClearanceGrid.MaximumAgentSize, nameof(agentSize));
        ValidateCost(cardinalCost, nameof(cardinalCost));
        ValidateCost(diagonalCost, nameof(diagonalCost));
        _steps = steps;
        _agentSize = agentSize;
        _cardinalCost = cardinalCost;
        _diagonalCost = diagonalCost;
    }

    /// <summary>
    /// Creates a four-way graph, whose only step class is the cardinal one.
    /// </summary>
    public UniformGridGraph(GridSteps<TOverlay> steps, int agentSize, TCost cardinalCost) : this(steps, agentSize, cardinalCost, cardinalCost)
    {
        if (steps.Connectivity != GridConnectivity.FourWay)
        {
            throw new ArgumentException("A single step cost describes four-way connectivity only. Pass a diagonal cost as well.", nameof(steps));
        }
    }

    public int NodeCount => _steps.Geometry.NodeCount;
    public int MaximumDegree => _steps.MaximumDegree;

    public int GetEdges(TIndex origin, Span<PathEdge<TIndex, TCost>> edges)
    {
        Span<GridStep> steps = stackalloc GridStep[_steps.MaximumDegree];
        int count = _steps.Enumerate(int.CreateChecked(origin), _agentSize, steps);
        for (int index = 0; index < count; index++)
        {
            GridStep step = steps[index];
            edges[index] = new PathEdge<TIndex, TCost>(TIndex.CreateChecked(step.Index), step.Distance > GridStep.CardinalDistance ? _diagonalCost : _cardinalCost);
        }

        return count;
    }

    /// <summary>
    /// Creates the admissible heuristic matching this graph's costs, geometry and connectivity.
    /// </summary>
    public GridHeuristic<TIndex, TCost> GetHeuristic()
    {
        if (_steps.Connectivity == GridConnectivity.FourWay)
        {
            return new GridHeuristic<TIndex, TCost>(_steps.Geometry, _cardinalCost);
        }

        return new GridHeuristic<TIndex, TCost>(_steps.Geometry, _cardinalCost, _diagonalCost);
    }

    private static void ValidateCost(TCost cost, string parameterName)
    {
        if (!TCost.IsFinite(cost) || TCost.IsNegative(cost))
        {
            throw new ArgumentOutOfRangeException(parameterName, cost, "The step cost must be finite and non-negative.");
        }
    }
}
