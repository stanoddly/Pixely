using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Pixely.Ecs.Generator.Tests;

public class GeneratorTests
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
    [TestCase("[Query] file ref partial struct Q { public Span<Position> P; }", "PXECS001")]
    [TestCase("file partial class Outer { [Query] ref partial struct Q { public Span<Position> P; } }", "PXECS001")]
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

    [Test]
    public void ValidBlueprintsAndSystemsCompile()
    {
        string source = Components + """
            namespace Game
            {
                using System.Collections.Generic;
                using Pixely.DependencyInjection;

                public struct Dead;

                [Blueprint]
                public partial struct Bullet
                {
                    public Position Position;
                    public Velocity Velocity;
                }

                public static partial class Blueprints
                {
                    [Blueprint]
                    internal readonly partial record struct Corpse(Position Position, Dead Dead);
                }

                [Query]
                internal ref partial struct Movement
                {
                    public ReadOnlySpan<Entity> Entities;
                    public Span<Position> Positions;
                }

                public sealed class Log;

                internal sealed class Shooter : ISystem
                {
                    public Shooter(Query<Movement> movement, Spawner<Bullet> bullets, ComponentAdder<Dead> dead, ComponentRemover<Velocity> stop,
                        EntityDestroyer destroyer, Log log, Log? optional, IEnumerable<Log> logs, Spawner<Blueprints.Corpse> corpses, in Log byValue)
                    {
                    }

                    public void Update()
                    {
                    }
                }

                internal static class Registration
                {
                    public static void Register(ServiceCollection services)
                    {
                        services.AddEcs();
                        services.AddSystem<Shooter>();
                    }
                }
            }
            """;

        (ImmutableArray<Diagnostic> generatorDiagnostics, Compilation output) = Run(source);

        Assert.That(generatorDiagnostics, Is.Empty);
        Assert.That(output.GetDiagnostics().Where(d => d.Severity >= DiagnosticSeverity.Warning), Is.Empty);
        Assert.That(output.SyntaxTrees.Any(tree => tree.ToString().Contains("builder.Spawner<global::Game.Bullet>()")), Is.True);
    }

    [TestCase("[Blueprint] struct B { public Position P; }", "PXECS006")]
    [TestCase("[Blueprint] ref partial struct B { public Position P; }", "PXECS006")]
    [TestCase("[Blueprint] partial struct B<T> { public Position P; }", "PXECS006")]
    [TestCase("class Outer { [Blueprint] partial struct B { public Position P; } }", "PXECS006")]
    [TestCase("[Blueprint] file partial struct B { public Position P; }", "PXECS006")]
    [TestCase("file partial class Outer { [Blueprint] partial struct B { public Position P; } }", "PXECS006")]
    [TestCase("[Blueprint] partial struct B { public string S; }", "PXECS007")]
    [TestCase("[Blueprint] partial struct B { public Entity E; }", "PXECS007")]
    [TestCase("[Blueprint] partial struct B { public Position A; public Position B2; }", "PXECS008")]
    [TestCase("[Blueprint] partial struct B { }", "PXECS009")]
    public void InvalidBlueprintReportsDiagnostic(string declaration, string expectedId)
    {
        (ImmutableArray<Diagnostic> diagnostics, _) = Run(Components + declaration);

        Assert.That(diagnostics.Select(d => d.Id), Is.EqualTo(new[] { expectedId }));
    }

    [TestCase("abstract class S : ISystem { public abstract void Update(); }", "S", "PXECS010")]
    [TestCase("file sealed class S : ISystem { public void Update() { } }", "S", "PXECS010")]
    [TestCase("class S : ISystem { public S() { } public S(int value) { } public void Update() { } }", "S", "PXECS011")]
    [TestCase("class S : ISystem { protected S() { } public void Update() { } }", "S", "PXECS011")]
    [TestCase("class Log; class S : ISystem { public S(ref Log log) { } public void Update() { } }", "S", "PXECS012")]
    [TestCase("class Log; class S : ISystem { public S(out Log log) { log = new Log(); } public void Update() { } }", "S", "PXECS012")]
    public void InvalidSystemRegistrationReportsDiagnostic(string declaration, string system, string expectedId)
    {
        string source = Components + declaration + $$"""

            static class Registration
            {
                static void Register(Pixely.DependencyInjection.ServiceCollection services) => services.AddSystem<{{system}}>();
            }
            """;

        (ImmutableArray<Diagnostic> diagnostics, _) = Run(source);

        Assert.That(diagnostics.Select(d => d.Id), Is.EqualTo(new[] { expectedId }));
    }

    // The generated code is outside the system, so what the call site can reach inside the system does not count.
    [TestCase("""
        class S : ISystem
        {
            private S() { }
            public void Update() { }
            static void Register(Pixely.DependencyInjection.ServiceCollection services) => services.AddSystem<S>();
        }
        """, "PXECS011")]
    [TestCase("""
        class Outer
        {
            private sealed class S : ISystem { public void Update() { } }
            static void Register(Pixely.DependencyInjection.ServiceCollection services) => services.AddSystem<S>();
        }
        """, "PXECS010")]
    public void SystemAccessibleOnlyAtTheCallSiteReportsDiagnostic(string declaration, string expectedId)
    {
        (ImmutableArray<Diagnostic> diagnostics, _) = Run(Components + declaration);

        Assert.That(diagnostics.Select(d => d.Id), Is.EqualTo(new[] { expectedId }));
    }

    [Test]
    public void KeywordNamesCompile()
    {
        string source = Components + """
            public static partial class @class
            {
                [Query]
                public ref partial struct @event
                {
                    public ReadOnlySpan<Entity> @this;
                    public Span<Position> @base;
                }

                [Blueprint]
                public partial struct @struct
                {
                    public Position @in;
                    public Velocity record;
                }

                [Blueprint]
                public partial record struct @record(Position @out);

                public sealed class @operator(Query<@event> query, Spawner<@struct> structs, Spawner<@record> records) : ISystem
                {
                    public void Update()
                    {
                        foreach (@event chunk in query)
                        {
                            chunk.@base[0] = chunk.@base[0];
                            structs.Spawn(new @struct { @in = chunk.@base[0], record = default });
                            records.Spawn(new @record(chunk.@base[0]));
                        }
                    }
                }

                public static void Register(Pixely.DependencyInjection.ServiceCollection services) => services.AddSystem<@operator>();
            }
            """;

        (ImmutableArray<Diagnostic> generatorDiagnostics, Compilation output) = Run(source);

        Assert.That(generatorDiagnostics, Is.Empty);
        Assert.That(output.GetDiagnostics().Where(d => d.Severity >= DiagnosticSeverity.Warning), Is.Empty);
    }

    private static (ImmutableArray<Diagnostic> GeneratorDiagnostics, Compilation Output) Run(string source)
    {
        string[] platformAssemblies = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
        IEnumerable<MetadataReference> references = platformAssemblies
            .Append(typeof(World).Assembly.Location)
            .Append(typeof(Pixely.DependencyInjection.ServiceCollection).Assembly.Location)
            .Append(typeof(IUpdatable).Assembly.Location)
            .Select(path => MetadataReference.CreateFromFile(path));

        CSharpParseOptions parseOptions = new CSharpParseOptions(LanguageVersion.Preview).WithFeatures([new KeyValuePair<string, string>("InterceptorsNamespaces", "Pixely.Ecs.Generated")]);
        CSharpCompilation compilation = CSharpCompilation.Create(
            "TestAssembly",
            [CSharpSyntaxTree.ParseText(source, parseOptions)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary).WithNullableContextOptions(NullableContextOptions.Enable));

        GeneratorDriver driver = CSharpGeneratorDriver.Create([new QueryGenerator().AsSourceGenerator(), new BlueprintGenerator().AsSourceGenerator(), new SystemGenerator().AsSourceGenerator()], parseOptions: parseOptions);
        driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out ImmutableArray<Diagnostic> diagnostics);
        return (diagnostics, output);
    }
}
