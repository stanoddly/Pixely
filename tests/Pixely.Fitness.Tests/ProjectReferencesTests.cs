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

        Assert.That(ProjectReferences.Violations(_root, new PeachArchitectureOptions("Peachy")), Is.Empty);
    }

    [Test]
    public void ExecutableMayReferenceAProjectOutsideTheGraph()
    {
        WriteProject("Peachy.Game");
        WriteProject("Peachy.Frontend", "Peachy.Game");
        WriteProject("Peachy.Executable", "Peachy.Game", "Peachy.Frontend", "DesktopOverlay");
        WriteProject("DesktopOverlay");

        Assert.That(ProjectReferences.Violations(_root, new PeachArchitectureOptions("Peachy")), Is.Empty);
    }

    [Test]
    public void ExecutableMissingAnArrowIsAViolation()
    {
        WriteProject("Peachy.Game");
        WriteProject("Peachy.Frontend", "Peachy.Game");
        WriteProject("Peachy.Executable", "Peachy.Game", "DesktopOverlay");
        WriteProject("DesktopOverlay");

        IReadOnlyList<string> violations = ProjectReferences.Violations(_root, new PeachArchitectureOptions("Peachy"));

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

        IReadOnlyList<string> violations = ProjectReferences.Violations(_root, new PeachArchitectureOptions("Peachy"));

        Assert.That(violations, Has.Count.EqualTo(1));
        Assert.That(violations[0], Does.StartWith("Peachy.Frontend:"));
    }

    private void WriteProject(string name, params string[] references)
    {
        string directory = Path.Combine(_root, "src", name);
        Directory.CreateDirectory(directory);
        string items = string.Concat(references.Select(reference => $"""<ProjectReference Include="..\{reference}\{reference}.csproj" />"""));
        File.WriteAllText(Path.Combine(directory, $"{name}.csproj"), $"""<Project Sdk="Microsoft.NET.Sdk"><ItemGroup>{items}</ItemGroup></Project>""");
    }
}
