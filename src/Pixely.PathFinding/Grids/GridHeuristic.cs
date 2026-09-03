using System.Numerics;

namespace Pixely.PathFinding.Grids;

/// <summary>
/// Estimates the cheapest remaining cost between two grid anchors from a lower bound per step class.
/// </summary>
/// <remarks>
/// Both costs must be lower bounds: neither may exceed the cheapest step of its kind anywhere on the grid. The estimate is
/// <c>diagonalSteps * min(diagonalCost, 2 * cardinalCost) + straightSteps * min(cardinalCost, diagonalCost)</c>, which is admissible for
/// any non-negative pair, because a diagonal displacement can always be walked as two cardinal steps and a straight displacement can
/// always be zig-zagged with diagonal steps. It degenerates to octile distance at <c>diagonalCost = sqrt(2) * cardinalCost</c> and to
/// Chebyshev distance at <c>diagonalCost = cardinalCost</c>. No square root is baked in, so an integral <typeparamref name="TCost"/> needs no rounding rule.
/// <para>
/// The estimate ignores connectivity, so passing a diagonal cost alongside <see cref="GridConnectivity.FourWay"/> is admissible but weak;
/// a four-way consumer passes only a cardinal cost. Products saturate at <see cref="IMinMaxValue{TCost}.MaxValue"/> rather than overflowing,
/// which <see cref="IndexedPathSearch{TIndex, TCost}"/> already treats as the lowest priority.
/// </para>
/// <para>The estimate is independent of agent size, so one heuristic serves every agent on the grid.</para>
/// </remarks>
public readonly struct GridHeuristic<TIndex, TCost> : IIndexedPathHeuristic<TIndex, TCost>
    where TIndex : unmanaged, IBinaryInteger<TIndex>, IMinMaxValue<TIndex>
    where TCost : unmanaged, INumber<TCost>, IMinMaxValue<TCost>
{
    private readonly int _width;
    private readonly TCost _diagonalStepCost;
    private readonly TCost _straightStepCost;

    /// <summary>
    /// Creates a heuristic for a grid whose cheapest step costs are known.
    /// </summary>
    /// <param name="cardinalCost">A lower bound on the cost of any cardinal step.</param>
    /// <param name="diagonalCost">A lower bound on the cost of any diagonal step.</param>
    public GridHeuristic(GridGeometry geometry, TCost cardinalCost, TCost diagonalCost)
    {
        ValidateCost(cardinalCost, nameof(cardinalCost));
        ValidateCost(diagonalCost, nameof(diagonalCost));
        _width = geometry.Width;
        _diagonalStepCost = TCost.Min(diagonalCost, MultiplySaturating(cardinalCost, 2));
        _straightStepCost = TCost.Min(cardinalCost, diagonalCost);
    }

    /// <summary>
    /// Creates a four-way heuristic, which is Manhattan distance scaled by the cardinal cost.
    /// </summary>
    public GridHeuristic(GridGeometry geometry, TCost cardinalCost) : this(geometry, cardinalCost, MultiplySaturating(cardinalCost, 2))
    {
    }

    public TCost EstimateCost(TIndex origin, TIndex destination)
    {
        int originOffset = int.CreateChecked(origin);
        int destinationOffset = int.CreateChecked(destination);
        int deltaX = Math.Abs(originOffset % _width - destinationOffset % _width);
        int deltaY = Math.Abs(originOffset / _width - destinationOffset / _width);
        int diagonalSteps = Math.Min(deltaX, deltaY);
        int straightSteps = Math.Max(deltaX, deltaY) - diagonalSteps;
        return AddSaturating(MultiplySaturating(_diagonalStepCost, diagonalSteps), MultiplySaturating(_straightStepCost, straightSteps));
    }

    private static TCost MultiplySaturating(TCost cost, int count)
    {
        if (count == 0 || cost == TCost.Zero)
        {
            return TCost.Zero;
        }

        TCost countCost = TCost.CreateSaturating(count);
        if (int.CreateSaturating(countCost) != count || cost > TCost.MaxValue / countCost)
        {
            return TCost.MaxValue;
        }

        TCost product = cost * countCost;
        return TCost.IsFinite(product) ? product : TCost.MaxValue;
    }

    private static TCost AddSaturating(TCost left, TCost right)
    {
        if (right > TCost.MaxValue - left)
        {
            return TCost.MaxValue;
        }

        TCost sum = left + right;
        return TCost.IsFinite(sum) ? sum : TCost.MaxValue;
    }

    private static void ValidateCost(TCost cost, string parameterName)
    {
        if (!TCost.IsFinite(cost) || TCost.IsNegative(cost))
        {
            throw new ArgumentOutOfRangeException(parameterName, cost, "The step cost must be finite and non-negative.");
        }
    }
}
