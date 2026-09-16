using Pixely.Fitness;

namespace Pixely.Fitness.Tests;

// The Peachy fixture is the smallest game the rules resolve: a Game with one state root, a Frontend and an Executable.
public sealed class PeachArchitectureTests
{
    [Test]
    public void EvaluateResolvesTheGameWithoutRecursing()
    {
        FitnessReport report = PeachArchitecture.Evaluate(new PeachArchitectureOptions("Peachy"));

        Assert.That(report["GameResolvesFromPrefix"].IsFit, Is.True, report.ToString());
    }
}
