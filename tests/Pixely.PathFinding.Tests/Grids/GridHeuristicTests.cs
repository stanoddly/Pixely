using Pixely.PathFinding.Grids;

namespace Pixely.PathFinding.Tests.Grids;

public sealed class GridHeuristicTests
{
    private static readonly (float Cardinal, float Diagonal)[] CostPairs =
    [
        (1f, 0.25f),
        (1f, 1f),
        (1f, MathF.Sqrt(2f)),
        (1f, 2f),
        (1f, 3f),
        (2f, 3f),
        (0.5f, 1f),
        (0f, 1f),
        (1f, 0f)
    ];

    [Test]
    public void EstimateCost_NeverExceedsTheCheapestPathOnAnUnobstructedEightWayGrid()
    {
        foreach ((float cardinal, float diagonal) in CostPairs)
        {
            AssertAdmissible(GridConnectivity.EightWay, cardinal, diagonal, new GridHeuristic<int, float>(new GridGeometry(6, 6), cardinal, diagonal));
        }
    }

    [Test]
    public void EstimateCost_NeverExceedsTheCheapestPathOnAnUnobstructedFourWayGrid()
    {
        foreach ((float cardinal, float diagonal) in CostPairs)
        {
            AssertAdmissible(GridConnectivity.FourWay, cardinal, diagonal, new GridHeuristic<int, float>(new GridGeometry(6, 6), cardinal));
        }
    }

    [Test]
    public void EstimateCost_IsExactOnAnUnobstructedGridAtTheOctileRatio()
    {
        GridGeometry geometry = new GridGeometry(6, 6);
        GridHeuristic<int, float> heuristic = new GridHeuristic<int, float>(geometry, 1f, MathF.Sqrt(2f));
        float[] costs = ExpandOpenGrid(geometry, GridConnectivity.EightWay, 1f, MathF.Sqrt(2f), 0);

        for (int destination = 0; destination < geometry.NodeCount; destination++)
        {
            Assert.That(heuristic.EstimateCost(0, destination), Is.EqualTo(costs[destination]).Within(1e-4f));
        }
    }

    [TestCase(3, 1, 1f, 3)]
    [TestCase(3, 3, 1f, 3)]
    [TestCase(0, 4, 1f, 4)]
    public void EstimateCost_DegeneratesToChebyshevWhenBothCostsMatch(int deltaX, int deltaY, float cardinal, int expected)
    {
        GridGeometry geometry = new GridGeometry(8, 8);
        GridHeuristic<int, float> heuristic = new GridHeuristic<int, float>(geometry, cardinal, cardinal);

        Assert.That(heuristic.EstimateCost(geometry.GetIndex(0, 0), geometry.GetIndex(deltaX, deltaY)), Is.EqualTo(expected).Within(1e-4f));
    }

    [Test]
    public void EstimateCost_SaturatesRatherThanOverflowing()
    {
        GridGeometry geometry = new GridGeometry(200, 200);
        GridHeuristic<int, byte> heuristic = new GridHeuristic<int, byte>(geometry, 4, 5);

        Assert.Multiple(() =>
        {
            Assert.That(heuristic.EstimateCost(0, geometry.GetIndex(199, 199)), Is.EqualTo(byte.MaxValue));
            Assert.That(heuristic.EstimateCost(0, geometry.GetIndex(3, 0)), Is.EqualTo(12));
        });
    }

    [Test]
    public void EstimateCost_StaysABoundWhenTheStepCountIsNotExactlyRepresentable()
    {
        // 2049 rounds to 2048 as a Half, so the estimate must stay a bound rather than collapsing to the saturated value.
        GridGeometry geometry = new GridGeometry(2050, 1);
        GridHeuristic<int, Half> heuristic = new GridHeuristic<int, Half>(geometry, Half.One, Half.One);

        Half estimate = heuristic.EstimateCost(0, geometry.GetIndex(2049, 0));

        Assert.Multiple(() =>
        {
            Assert.That(estimate, Is.LessThanOrEqualTo((Half)2049));
            Assert.That(estimate, Is.GreaterThanOrEqualTo((Half)2048));
        });
    }

    [Test]
    public void EstimateCost_SaturatesWhenTheStepClassesSumBeyondTheRange()
    {
        GridGeometry geometry = new GridGeometry(200, 200);
        GridHeuristic<int, byte> heuristic = new GridHeuristic<int, byte>(geometry, 3, 3);

        Assert.Multiple(() =>
        {
            Assert.That(heuristic.EstimateCost(0, geometry.GetIndex(100, 50)), Is.EqualTo(byte.MaxValue));
            Assert.That(heuristic.EstimateCost(0, geometry.GetIndex(40, 20)), Is.EqualTo(120));
        });
    }

    [Test]
    public void Constructor_SaturatesTheDoubledCardinalCostRatherThanWrapping()
    {
        GridGeometry geometry = new GridGeometry(8, 8);
        GridHeuristic<int, byte> heuristic = new GridHeuristic<int, byte>(geometry, 200);

        Assert.Multiple(() =>
        {
            Assert.That(heuristic.EstimateCost(0, geometry.GetIndex(1, 1)), Is.EqualTo(byte.MaxValue));
            Assert.That(heuristic.EstimateCost(0, geometry.GetIndex(1, 0)), Is.EqualTo(200));
        });
    }

    [TestCase(float.NaN, 1f)]
    [TestCase(-1f, 1f)]
    [TestCase(1f, float.PositiveInfinity)]
    public void Constructor_RejectsCostsThatAreNotFiniteAndNonNegative(float cardinal, float diagonal)
    {
        Assert.That(() => new GridHeuristic<int, float>(new GridGeometry(4, 4), cardinal, diagonal), Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    private static void AssertAdmissible(GridConnectivity connectivity, float cardinal, float diagonal, GridHeuristic<int, float> heuristic)
    {
        GridGeometry geometry = new GridGeometry(6, 6);
        for (int start = 0; start < geometry.NodeCount; start++)
        {
            float[] costs = ExpandOpenGrid(geometry, connectivity, cardinal, diagonal, start);
            for (int destination = 0; destination < geometry.NodeCount; destination++)
            {
                Assert.That(heuristic.EstimateCost(start, destination), Is.LessThanOrEqualTo(costs[destination] + 1e-4f), $"{connectivity} costs ({cardinal}, {diagonal}) from {start} to {destination}");
            }
        }
    }

    private static float[] ExpandOpenGrid(GridGeometry geometry, GridConnectivity connectivity, float cardinal, float diagonal, int start)
    {
        ClearanceGrid clearance = new ClearanceGrid(geometry);
        clearance.Rebuild(new bool[geometry.NodeCount]);
        UniformGridGraph<int, float, NoGridOverlay> graph = new UniformGridGraph<int, float, NoGridOverlay>(new GridSteps<NoGridOverlay>(clearance, connectivity, default), 1, cardinal, diagonal);
        IndexedPathSearch<int, float> search = new IndexedPathSearch<int, float>();
        float[] costs = new float[geometry.NodeCount];
        search.ExpandTree(graph, start, costs, new int[geometry.NodeCount]);
        return costs;
    }
}
