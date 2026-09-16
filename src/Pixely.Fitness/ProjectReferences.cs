using System.Xml.Linq;

namespace Pixely.Fitness;

/// <summary>
/// The project files carry exactly the arrows of the document. Each project sits at src/Foo.Part/Foo.Part.csproj.
/// </summary>
internal static class ProjectReferences
{
    internal static IReadOnlyList<string> Violations(string repositoryRoot, PeachArchitectureOptions options)
    {
        string game = "Game";
        string? rendering = options.Rendering == null ? null : "Frontend.Rendering";
        string? audio = options.Audio == null ? null : "Frontend.Audio";
        string? ai = options.Ai == null ? null : "Ai";
        string? scenario = options.Scenario == null ? null : "Scenario";
        string[] outputs = new[] { rendering, audio }.OfType<string>().ToArray();
        string[] actors = new[] { ai, scenario }.OfType<string>().ToArray();
        List<string> violations = new List<string>();
        Check(game);
        foreach (string part in outputs.Concat(actors))
        {
            Check(part, game);
        }

        Check("Frontend", outputs.Prepend(game).ToArray());
        Check("Executable", outputs.Concat(actors).Concat([game, "Frontend"]).ToArray());
        return violations;

        void Check(string part, params string[] expectedParts)
        {
            string path = Path.Combine(repositoryRoot, "src", $"{options.GamePrefix}.{part}", $"{options.GamePrefix}.{part}.csproj");
            string directory = Path.GetDirectoryName(path)!;
            string[] actual = XDocument.Load(path).Descendants("ProjectReference")
                .Select(reference => reference.Attribute("Include")?.Value ?? throw new InvalidOperationException($"ProjectReference in {path} has no Include attribute."))
                .Select(reference => Path.GetRelativePath(repositoryRoot, Path.GetFullPath(Path.Combine(directory, reference.Replace('\\', Path.DirectorySeparatorChar)))))
                .Order(StringComparer.Ordinal)
                .ToArray();
            string[] expected = expectedParts
                .Select(expectedPart => Path.Combine("src", $"{options.GamePrefix}.{expectedPart}", $"{options.GamePrefix}.{expectedPart}.csproj"))
                .Order(StringComparer.Ordinal)
                .ToArray();
            if (!actual.SequenceEqual(expected, StringComparer.Ordinal))
            {
                violations.Add($"{options.GamePrefix}.{part}: references [{string.Join(", ", actual)}], expected [{string.Join(", ", expected)}]");
            }
        }
    }
}
