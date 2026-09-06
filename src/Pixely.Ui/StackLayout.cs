namespace Pixely.Ui;

/// <summary>
/// Places children one after another along an axis, separated by a gap. Owns the Grow
/// distribution, since only a stack knows the orientation, the gaps and the sibling order that
/// the leftover space depends on.
/// </summary>
public sealed class StackLayout : ILayout
{
    public static StackLayout Vertical { get; } = new(Orientation.Vertical, 0);
    public static StackLayout Horizontal { get; } = new(Orientation.Horizontal, 0);

    private readonly List<int> _growIndices = new();
    private readonly List<float> _growWeights = new();
    private readonly List<int> _growAllocations = new();

    public StackLayout(Orientation orientation, int gap = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(gap);

        Orientation = orientation;
        Gap = gap;
    }

    public Orientation Orientation { get; }

    public int Gap { get; }

    public Vector2Int MeasureChildren(ILayoutHost host, Constraints contentConstraints)
    {
        int count = host.ChildCount;
        if (count == 0)
        {
            return default;
        }

        int gaps = Gap * (count - 1);
        int mainTotal = 0;
        int crossMax = 0;

        _growIndices.Clear();
        _growWeights.Clear();

        for (int i = 0; i < count; i++)
        {
            Sizing mainSizing = host.GetResolvedSizing(i, Orientation);

            // A live Grow child has no size yet; it is measured below, once its share is known.
            // One that degraded to Fit is not live and is measured here like any other child.
            if (mainSizing.Mode == SizingMode.Grow)
            {
                _growIndices.Add(i);
                _growWeights.Add(mainSizing.Factor);
                continue;
            }

            Vector2Int size = host.MeasureChild(i, Remaining(contentConstraints, mainTotal + gaps));
            mainTotal += GetMain(size);
            crossMax = Math.Max(crossMax, GetCross(size));
        }

        if (_growIndices.Count > 0)
        {
            // Grow only survives degradation on a definite axis, so the budget is always a real number.
            int budget = host.GetDefiniteContentExtent(Orientation)!.Value;
            int surplus = Math.Max(0, budget - mainTotal - gaps);

            Distribute(surplus, _growWeights, _growAllocations);

            for (int i = 0; i < _growIndices.Count; i++)
            {
                int childIndex = _growIndices[i];
                int allocation = _growAllocations[i];

                Vector2Int size = host.MeasureChildWithExtent(
                    childIndex,
                    Orientation,
                    allocation,
                    Remaining(contentConstraints, mainTotal + gaps));

                mainTotal += GetMain(size);
                crossMax = Math.Max(crossMax, GetCross(size));
            }
        }

        return Compose(mainTotal + gaps, crossMax);
    }

    public void ArrangeChildren(ILayoutHost host, Rectangle contentBounds)
    {
        int count = host.ChildCount;
        int offset = Orientation == Orientation.Horizontal ? contentBounds.X : contentBounds.Y;

        for (int i = 0; i < count; i++)
        {
            Element child = host.GetChild(i);
            Vector2Int slotSize = new(
                child.DesiredSize.X + child.Margin.Horizontal,
                child.DesiredSize.Y + child.Margin.Vertical);

            int mainSize = GetMain(slotSize);

            Rectangle slot = Orientation == Orientation.Horizontal
                ? new Rectangle(offset, contentBounds.Y, mainSize, contentBounds.Height)
                : new Rectangle(contentBounds.X, offset, contentBounds.Width, mainSize);

            host.ArrangeChild(i, slot);

            offset += mainSize + Gap;
        }
    }

    /// <summary>
    /// Splits <paramref name="surplus"/> by weight using floored shares, then hands the leftover
    /// pixels to the largest fractional remainders (ties by order). Weights are arbitrary floats,
    /// so plain division would lose or invent pixels; this always totals exactly the surplus.
    /// </summary>
    private static void Distribute(int surplus, List<float> weights, List<int> allocations)
    {
        allocations.Clear();

        float totalWeight = 0f;
        foreach (float weight in weights)
        {
            totalWeight += weight;
        }

        if (totalWeight <= 0f)
        {
            for (int i = 0; i < weights.Count; i++)
            {
                allocations.Add(0);
            }

            return;
        }

        Span<float> remainders = weights.Count <= 32 ? stackalloc float[weights.Count] : new float[weights.Count];
        int assigned = 0;

        for (int i = 0; i < weights.Count; i++)
        {
            float exact = surplus * (weights[i] / totalWeight);
            int floor = (int)MathF.Floor(exact);
            allocations.Add(floor);
            remainders[i] = exact - floor;
            assigned += floor;
        }

        int residual = surplus - assigned;

        for (int step = 0; step < residual; step++)
        {
            int best = -1;
            float bestRemainder = float.NegativeInfinity;

            for (int i = 0; i < remainders.Length; i++)
            {
                if (remainders[i] > bestRemainder)
                {
                    bestRemainder = remainders[i];
                    best = i;
                }
            }

            if (best < 0)
            {
                break;
            }

            allocations[best]++;
            remainders[best] = float.NegativeInfinity;
        }
    }

    private Constraints Remaining(Constraints contentConstraints, int consumed)
    {
        return Orientation == Orientation.Horizontal
            ? contentConstraints with { MinWidth = 0, MaxWidth = ReduceMax(contentConstraints.MaxWidth, consumed) }
            : contentConstraints with { MinHeight = 0, MaxHeight = ReduceMax(contentConstraints.MaxHeight, consumed) };
    }

    private static int ReduceMax(int max, int consumed)
    {
        return max == Constraints.Unbounded ? Constraints.Unbounded : Math.Max(0, max - consumed);
    }

    private int GetMain(Vector2Int size) => Orientation == Orientation.Horizontal ? size.X : size.Y;

    private int GetCross(Vector2Int size) => Orientation == Orientation.Horizontal ? size.Y : size.X;

    private Vector2Int Compose(int main, int cross) =>
        Orientation == Orientation.Horizontal ? new Vector2Int(main, cross) : new Vector2Int(cross, main);
}
