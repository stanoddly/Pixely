using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Pixely.Ecs.Generator.Tests;

public class QueryGeneratorTests
{
    private const string Components = """
        using System;
        using Pixely.Ecs;

        public struct Position { public float X; }
        public struct Velocity { public float X; }
        public struct Frozen;

        """;

    [Test]
    public void ValidQueriesCompile()
    {
        string source = Components + """
            namespace Game
            {
                [Query]
                [With<Velocity>]
                [Without<Frozen>]
                internal ref partial struct Movement
                {
                    public ReadOnlySpan<Entity> Entities;
                    public Span<Position> Positions;
                }

                public static partial class Systems
                {
                    [Query]
                    private readonly ref partial struct Velocities
                    {
                        public readonly ReadOnlySpan<Velocity> Values;
                    }

                    public static int Count(World world)
                    {
                        int count = 0;
                        foreach (Velocities chunk in world.Query<Velocities>())
                        {
                            count += chunk.Count;
                        }

                        foreach (Movement chunk in world.Query<Movement>())
                        {
                            count += chunk.Count;
                        }

                        foreach (GlobalQuery chunk in world.Query<GlobalQuery>())
                        {
                            count += chunk.Count;
                        }

                        return count;
                    }
                }
            }

            [Query]
            ref partial struct GlobalQuery
            {
                public Span<Position> Positions;
            }
            """;

        (ImmutableArray<Diagnostic> generatorDiagnostics, Compilation output) = Run(source);

        Assert.That(generatorDiagnostics, Is.Empty);
        Assert.That(output.GetDiagnostics().Where(d => d.Severity >= DiagnosticSeverity.Warning), Is.Empty);
    }

    [TestCase("[Query] partial struct Q { public Span<Position> P; }", "PXECS001")]
    [TestCase("[Query] ref struct Q { public Span<Position> P; }", "PXECS001")]
    [TestCase("[Query] ref partial struct Q<T> { public Span<Position> P; }", "PXECS001")]
    [TestCase("class Outer { [Query] ref partial struct Q { public Span<Position> P; } }", "PXECS001")]
    [TestCase("[Query] ref partial struct Q { public int P; }", "PXECS002")]
    [TestCase("[Query] ref partial struct Q { public Span<Entity> E; }", "PXECS002")]
    [TestCase("[Query] ref partial struct Q { public Span<string> S; }", "PXECS002")]
    [TestCase("[Query] ref partial struct Q { public Span<Position> A; public ReadOnlySpan<Position> B; }", "PXECS003")]
    [TestCase("[Query] [With<Position>] ref partial struct Q { }", "PXECS004")]
    [TestCase("[Query] ref partial struct Q { public ReadOnlySpan<Entity> E; }", "PXECS004")]
    [TestCase("[Query] [Without<Position>] ref partial struct Q { public Span<Position> P; }", "PXECS005")]
    public void InvalidQueryReportsDiagnostic(string declaration, string expectedId)
    {
        (ImmutableArray<Diagnostic> diagnostics, _) = Run(Components + declaration);

        Assert.That(diagnostics.Select(d => d.Id), Is.EqualTo(new[] { expectedId }));
    }

    private static (ImmutableArray<Diagnostic> GeneratorDiagnostics, Compilation Output) Run(string source)
    {
        string[] platformAssemblies = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
        IEnumerable<MetadataReference> references = platformAssemblies
            .Append(typeof(World).Assembly.Location)
            .Select(path => MetadataReference.CreateFromFile(path));

        CSharpParseOptions parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
        CSharpCompilation compilation = CSharpCompilation.Create(
            "TestAssembly",
            [CSharpSyntaxTree.ParseText(source, parseOptions)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary).WithNullableContextOptions(NullableContextOptions.Enable));

        GeneratorDriver driver = CSharpGeneratorDriver.Create([new QueryGenerator().AsSourceGenerator()], parseOptions: parseOptions);
        driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out ImmutableArray<Diagnostic> diagnostics);
        return (diagnostics, output);
    }
}
