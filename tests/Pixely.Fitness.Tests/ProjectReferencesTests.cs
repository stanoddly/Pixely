namespace Pixely.Fitness.Tests;

// Each test writes the project files of the Peachy fixture, a Game, a Frontend and an Executable, under a temporary repository root.
public sealed class ProjectReferencesTests
{
    private string _root = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Directory.CreateTempSubdirectory("Pixely.Fitness.Tests.").FullName;
    }

    [TearDown]
    public void TearDown()
    {
        Directory.Delete(_root, true);
    }

    [Test]
    public void ProjectsReferencingExactlyTheirArrowsAreFit()
    {
        WriteProject("Peachy.Game");
        WriteProject("Peachy.Frontend", "Peachy.Game");
        WriteProject("Peachy.Executable", "Peachy.Game", "Peachy.Frontend");

        FitnessReport report = Evaluate();

        Assert.That(report["GameResolvesFromPrefix"].IsFit, Is.True, report.ToString());
        Assert.That(report["ProjectReferencesMatchTheGraph"].IsFit, Is.True, report.ToString());
    }

    [Test]
    public void ExecutableMayReferenceAProjectOutsideTheGraph()
    {
        WriteProject("Peachy.Game");
        WriteProject("Peachy.Frontend", "Peachy.Game");
        WriteProject("Peachy.Executable", "Peachy.Game", "Peachy.Frontend", "DesktopOverlay");
        WriteProject("DesktopOverlay");

        FitnessReport report = Evaluate();

        Assert.That(report["ProjectReferencesMatchTheGraph"].IsFit, Is.True, report.ToString());
    }

    [Test]
    public void ExecutableMissingAnArrowIsAViolation()
    {
        WriteProject("Peachy.Game");
        WriteProject("Peachy.Frontend", "Peachy.Game");
        WriteProject("Peachy.Executable", "Peachy.Game", "DesktopOverlay");
        WriteProject("DesktopOverlay");

        IReadOnlyList<string> violations = Evaluate()["ProjectReferencesMatchTheGraph"].Violations;

        Assert.That(violations, Has.Count.EqualTo(1));
        Assert.That(violations[0], Does.StartWith("Peachy.Executable:").And.Contains("expected at least"));
    }

    [Test]
    public void FrontendReferencingAProjectOutsideTheGraphIsAViolation()
    {
        WriteProject("Peachy.Game");
        WriteProject("Peachy.Frontend", "Peachy.Game", "DesktopOverlay");
        WriteProject("Peachy.Executable", "Peachy.Game", "Peachy.Frontend");
        WriteProject("DesktopOverlay");

        IReadOnlyList<string> violations = Evaluate()["ProjectReferencesMatchTheGraph"].Violations;

        Assert.That(violations, Has.Count.EqualTo(1));
        Assert.That(violations[0], Does.StartWith("Peachy.Frontend:"));
    }

    [Test]
    public void RepositoryRootWithoutTheGameProjectIsAViolation()
    {
        FitnessReport report = Evaluate();

        Assert.That(report["GameResolvesFromPrefix"].Violations, Is.EqualTo(new[] { $"repository root has no src/Peachy.Game/Peachy.Game.csproj: {_root}" }));
        Assert.That(report["ProjectReferencesMatchTheGraph"].IsFit, Is.True, report.ToString());
    }

    [Test]
    public void CopyWithAnotherRepositoryRootResolvesAgain()
    {
        WriteProject("Peachy.Game");
        WriteProject("Peachy.Frontend", "Peachy.Game", "DesktopOverlay");
        WriteProject("Peachy.Executable", "Peachy.Game", "Peachy.Frontend");
        WriteProject("DesktopOverlay");
        PeachArchitectureOptions options = new PeachArchitectureOptions("Peachy");
        PeachArchitecture.Evaluate(options);

        FitnessReport report = PeachArchitecture.Evaluate(options with { RepositoryRoot = _root });

        Assert.That(report["ProjectReferencesMatchTheGraph"].IsFit, Is.False, report.ToString());
    }

    private FitnessReport Evaluate()
    {
        return PeachArchitecture.Evaluate(new PeachArchitectureOptions("Peachy") { RepositoryRoot = _root });
    }

    private void WriteProject(string name, params string[] references)
    {
        string directory = Path.Combine(_root, "src", name);
        Directory.CreateDirectory(directory);
        string items = string.Concat(references.Select(reference => $"""<ProjectReference Include="..\{reference}\{reference}.csproj" />"""));
        File.WriteAllText(Path.Combine(directory, $"{name}.csproj"), $"""<Project Sdk="Microsoft.NET.Sdk"><ItemGroup>{items}</ItemGroup></Project>""");
    }
}
