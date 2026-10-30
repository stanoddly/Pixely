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
    public void ValidQueriesAndSystemsCompile()
    {
        string source = Components + """
            namespace Game
            {
                using Pixely.DependencyInjection;

                public struct Dead : ITag;

                internal sealed class Movement : Query<ReadOnlySpan<Entity>, Span<Position>, ReadOnlySpan<Velocity>>, IWithout<Frozen>;

                public static class Queries
                {
                    public sealed class Corpses : Query<ReadOnlySpan<Entity>>, IWith<Dead>;
                }

                internal sealed class Mover : ISystem
                {
                    private readonly Movement _movement;

                    public Mover(Movement movement, Queries.Corpses corpses, Query<ReadOnlySpan<Velocity>> velocities, ComponentLookup<Position> positionLookup,
                        ReadOnlyComponentLookup<Dead> deadLookup, ComponentLookup<Position, Velocity, Dead> lookup)
                    {
                        _movement = movement;
                    }

                    public void Update()
                    {
                        foreach ((ReadOnlySpan<Entity> entities, Span<Position> positions, ReadOnlySpan<Velocity> speeds) in _movement)
                        {
                            for (int i = 0; i < positions.Length; i++)
                            {
                                positions[i].X += speeds[i].X;
                            }
                        }
                    }
                }

                internal static class Registration
                {
                    public static void Register(ServiceCollection services)
                    {
                        services.AddEcs();
                        services.AddSystem<Mover>();
                    }
                }
            }
            """;

        (ImmutableArray<Diagnostic> generatorDiagnostics, Compilation output) = Run(source);

        Assert.That(generatorDiagnostics, Is.Empty);
        Assert.That(output.GetDiagnostics().Where(d => d.Severity >= DiagnosticSeverity.Warning), Is.Empty);
        string interceptors = output.SyntaxTrees.Single(tree => tree.FilePath.EndsWith("SystemInterceptors.g.cs")).ToString();
        Assert.That(interceptors, Does.Contain("AddSystem_0").And.Contain("builder.Query<global::Game.Movement>()"));
        Assert.That(interceptors, Does.Contain("builder.Query<global::Game.Queries.Corpses>()").And.Contain("builder.Query<global::Pixely.Ecs.Query<global::System.ReadOnlySpan<global::Velocity>>>()"));
        Assert.That(interceptors, Does.Contain("builder.ComponentLookup<global::Position>()").And.Contain("builder.ReadOnlyComponentLookup<global::Game.Dead>()"));
        Assert.That(interceptors, Does.Contain("builder.ComponentLookup<global::Position, global::Velocity, global::Game.Dead>()"));
    }

    [TestCase("abstract class Q : Query<ReadOnlySpan<Position>>;", "PXECS001")]
    [TestCase("sealed class Q : Query<ReadOnlySpan<Position>> { public Q(int value) { } }", "PXECS001")]
    [TestCase("sealed class Q : Query<ReadOnlySpan<Position>> { internal Q() { } }", "PXECS001")]
    [TestCase("sealed class Q : Query<ReadOnlySpan<Entity>>;", "PXECS004")]
    [TestCase("sealed class Q : Query<ReadOnlySpan<Entity>>, IWithout<Position>;", "PXECS004")]
    [TestCase("sealed class Q : Query<ReadOnlySpan<Position>>, IWithout<Position>;", "PXECS005")]
    [TestCase("sealed class Q : Query<ReadOnlySpan<Entity>>, IWith<Frozen>, IWithout<Frozen>;", "PXECS005")]
    [TestCase("sealed class Q : Query<Optional<ReadOnlySpan<Position>>>;", "PXECS004")]
    [TestCase("sealed class Q : Query<ReadOnlySpan<Position>, Optional<ReadOnlySpan<Velocity>>>, IWith<Velocity>;", "PXECS016")]
    [TestCase("sealed class Q : Query<ReadOnlySpan<Position>, Optional<Span<Velocity>>>, IWithout<Velocity>;", "PXECS016")]
    public void InvalidQueryOfASystemReportsDiagnostic(string declaration, string expectedId)
    {
        string source = Components + declaration + """

            sealed class S(Q query) : ISystem
            {
                public void Update()
                {
                }
            }

            static class Registration
            {
                static void Register(Pixely.DependencyInjection.ServiceCollection services) => services.AddSystem<S>();
            }
            """;

        (ImmutableArray<Diagnostic> diagnostics, _) = Run(source);

        Assert.That(diagnostics.Select(d => d.Id), Is.EqualTo(new[] { expectedId }));
    }

    // Both would implement the same internal member, so a query has at most one IWith<...> and one IWithout<...>.
    [TestCase("IWith<Position>, IWith<Velocity>")]
    [TestCase("IWithout<Position>, IWithout<Velocity>")]
    public void TwoFiltersOfOneKindAreRejectedByTheCompiler(string filters)
    {
        string source = Components + $"sealed class Q : Query<ReadOnlySpan<Entity>>, {filters};";

        (_, Compilation output) = Run(source);

        Assert.That(output.GetDiagnostics().Select(d => d.Id), Does.Contain("CS8705"));
    }

    // Only IWith<...> implements the internal member of IWith.
    [Test]
    public void HandWrittenFilterIsRejectedByTheCompiler()
    {
        (_, Compilation output) = Run(Components + "sealed class Q : Query<ReadOnlySpan<Position>>, IWith;");

        Assert.That(output.GetDiagnostics().Select(d => d.Id), Does.Contain("CS0535"));
    }

    [TestCase("sealed class Q : Query<Span<Entity>, ReadOnlySpan<Position>>;", "PXECS002")]
    [TestCase("sealed class Q : Query<int>;", "PXECS002")]
    [TestCase("sealed class Q : Query<Span<string>>;", "PXECS002")]
    [TestCase("public struct T : ITag; sealed class Q : Query<ReadOnlySpan<T>>;", "PXECS002")]
    [TestCase("ref struct R; sealed class Q : Query<Span<R>>;", "PXECS002")]
    [TestCase("sealed class Q : Query<ReadOnlySpan<Position>, Optional<ReadOnlySpan<Entity>>>;", "PXECS002")]
    [TestCase("sealed class Q : Query<Optional<Position>>;", "PXECS002")]
    [TestCase("sealed class Q : Query<Optional<Optional<Span<Position>>>>;", "PXECS002")]
    [TestCase("public struct T : ITag; sealed class Q : Query<ReadOnlySpan<Position>, Optional<ReadOnlySpan<T>>>;", "PXECS002")]
    [TestCase("sealed class Q : Query<ReadOnlySpan<Position>, Span<Position>>;", "PXECS003")]
    [TestCase("sealed class Q : Query<ReadOnlySpan<Position>, Optional<Span<Position>>>;", "PXECS003")]
    [TestCase("static class R { static void Read(Pixely.DependencyInjection.ServiceCollection services) => services.AddQuery<Query<Span<Entity>>>(); }", "PXECS002")]
    [TestCase("static class R { static void Read(Pixely.DependencyInjection.ServiceCollection services) => services.AddQuery<Query<ReadOnlySpan<Position>, ReadOnlySpan<Position>>>(); }", "PXECS003")]
    public void InvalidSpanTypeArgumentReportsDiagnostic(string declaration, string expectedId)
    {
        (ImmutableArray<Diagnostic> diagnostics, _) = Run(Components + declaration);

        Assert.That(diagnostics.Select(d => d.Id), Is.EqualTo(new[] { expectedId }));
    }

    // Every span a Query<...> names is registered once, wherever it is written. An open type argument is registered where it is closed.
    [Test]
    public void SpansOfEveryQueryAreRegisteredOnce()
    {
        string source = Components + """
            sealed class Movement : Query<ReadOnlySpan<Entity>, Span<Position>, ReadOnlySpan<Velocity>>;

            sealed class Positions : Query<ReadOnlySpan<Position>>;

            sealed class Steering : Query<Optional<Span<Velocity>>, Optional<ReadOnlySpan<Position>>>, IWith<Frozen>;

            sealed class S : ISystem
            {
                public S(Query<Span<Velocity>> velocities)
                {
                }

                public void Update()
                {
                }
            }

            static class Helpers
            {
                static int Count<T1>(Query<T1> query) where T1 : allows ref struct => 0;
            }
            """;

        (ImmutableArray<Diagnostic> generatorDiagnostics, Compilation output) = Run(source);

        Assert.That(generatorDiagnostics, Is.Empty);
        Assert.That(output.GetDiagnostics().Where(d => d.Severity >= DiagnosticSeverity.Warning), Is.Empty);
        string registration = output.SyntaxTrees.Single(tree => tree.FilePath.EndsWith("QueryTerms.g.cs")).ToString();
        string[] calls = registration.Split('\n').Select(line => line.Trim()).Where(line => line.StartsWith("global::Pixely.Ecs.QueryTerms.")).ToArray();
        Assert.That(calls, Is.EquivalentTo(new[]
        {
            "global::Pixely.Ecs.QueryTerms.RegisterSpan<global::Position>();",
            "global::Pixely.Ecs.QueryTerms.RegisterReadOnlySpan<global::Position>();",
            "global::Pixely.Ecs.QueryTerms.RegisterReadOnlySpan<global::Velocity>();",
            "global::Pixely.Ecs.QueryTerms.RegisterSpan<global::Velocity>();",
            "global::Pixely.Ecs.QueryTerms.RegisterOptionalSpan<global::Velocity>();",
            "global::Pixely.Ecs.QueryTerms.RegisterOptionalReadOnlySpan<global::Position>();"
        }));
    }

    [Test]
    public void ValidSpawnersAndSystemsCompile()
    {
        string source = Components + """
            namespace Game
            {
                using System.Collections.Generic;
                using Pixely.DependencyInjection;

                public struct Dead : ITag;

                public sealed class Bullets : Spawner<Position, Velocity>
                {
                    public Entity Spawn(Position at) => Spawn(at, new Velocity { X = 1 });
                }

                public static class Spawners
                {
                    internal sealed class Corpses : Spawner<Position>, IWith<Dead>;
                }

                internal sealed class Movement : Query<ReadOnlySpan<Entity>, Span<Position>>;

                public sealed class Log;

                internal sealed class Shooter : ISystem
                {
                    private readonly Bullets _bullets;
                    private readonly Spawners.Corpses _corpses;
                    private readonly Spawner<Velocity> _velocities;

                    public Shooter(Movement movement, Log log, Bullets bullets, Log? optional, ComponentAdder<Dead> dead, IEnumerable<Log> logs, ComponentRemover<Velocity> stop,
                        in Log byValue, EntityDestroyer destroyer, Spawners.Corpses corpses, IReadOnlyList<Log> list, Spawner<Velocity> velocities, World world)
                    {
                        _bullets = bullets;
                        _corpses = corpses;
                        _velocities = velocities;
                    }

                    public void Update()
                    {
                        _bullets.Spawn(new Position());
                        _corpses.Spawn(new Position());
                        _velocities.Spawn(new Velocity());
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
        string interceptors = output.SyntaxTrees.Single(tree => tree.FilePath.EndsWith("SystemInterceptors.g.cs")).ToString();
        Assert.That(interceptors, Does.Contain("builder.Spawner<global::Game.Bullets>()").And.Contain("builder.Spawner<global::Game.Spawners.Corpses>()"));
        Assert.That(interceptors, Does.Contain("builder.Spawner<global::Pixely.Ecs.Spawner<global::Velocity>>()"));
        Assert.That(interceptors, Does.Contain("sp.GetRequiredService<global::System.Collections.Generic.IReadOnlyList<global::Game.Log>>()"));
        Assert.That(interceptors, Does.Contain("new global::Game.Shooter(builder.Query<global::Game.Movement>(), sp.GetRequiredService<global::Game.Log>(), builder.Spawner<global::Game.Bullets>(), "));
        Assert.That(interceptors, Does.Contain("builder.Spawner<global::Pixely.Ecs.Spawner<global::Velocity>>(), sp.GetRequiredService<global::Pixely.Ecs.World>()), order));"));
    }

    [TestCase("abstract class P : Spawner<Position>;", "PXECS006")]
    [TestCase("sealed class P : Spawner<Position> { public P(int value) { } }", "PXECS006")]
    [TestCase("sealed class P : Spawner<Position> { internal P() { } }", "PXECS006")]
    [TestCase("sealed class P : Spawner<Entity>;", "PXECS007")]
    [TestCase("public struct Mark : ITag; sealed class P : Spawner<Position, Mark>;", "PXECS007")]
    [TestCase("sealed class P : Spawner<Position, Position>;", "PXECS008")]
    [TestCase("public struct Mark : ITag; sealed class P : Spawner<Position>, IWith<Mark, Mark>;", "PXECS008")]
    [TestCase("sealed class P : Spawner<Position>, IWith<Velocity>;", "PXECS009")]
    [TestCase("public struct Mark : ITag; sealed class P : Spawner<Position>, IWithout<Mark>;", "PXECS009")]
    public void InvalidSpawnerOfASystemReportsDiagnostic(string declaration, string expectedId)
    {
        string source = Components + declaration + """

            sealed class S(P spawner) : ISystem
            {
                public void Update()
                {
                }
            }

            static class Registration
            {
                static void Register(Pixely.DependencyInjection.ServiceCollection services) => services.AddSystem<S>();
            }
            """;

        (ImmutableArray<Diagnostic> diagnostics, _) = Run(source);

        Assert.That(diagnostics.Select(d => d.Id), Is.EqualTo(new[] { expectedId }));
    }

    [TestCase("abstract class S : ISystem { public abstract void Update(); }", "S", "PXECS010")]
    [TestCase("file sealed class S : ISystem { public void Update() { } }", "S", "PXECS010")]
    [TestCase("class Outer<T> { public sealed class S : ISystem { public void Update() { } } }", "Outer<T>.S", "PXECS010")]
    [TestCase("class Outer<T> { public sealed class S : ISystem { public void Update() { } } }", "Outer<T[]>.S", "PXECS010")]
    [TestCase("class S : ISystem { public S() { } public S(int value) { } public void Update() { } }", "S", "PXECS011")]
    [TestCase("class S : ISystem { protected S() { } public void Update() { } }", "S", "PXECS011")]
    [TestCase("class Log; class S : ISystem { public S(ref Log log) { } public void Update() { } }", "S", "PXECS012")]
    [TestCase("class Log; class S : ISystem { public S(out Log log) { log = new Log(); } public void Update() { } }", "S", "PXECS012")]
    [TestCase("class S : ISystem { public S(ComponentLookup<Position, Position> l) { } public void Update() { } }", "S", "PXECS008")]
    [TestCase("class S : ISystem { public S(ReadOnlyComponentLookup<Position, Velocity, Position> l) { } public void Update() { } }", "S", "PXECS008")]
    public void InvalidSystemRegistrationReportsDiagnostic(string declaration, string system, string expectedId)
    {
        string source = Components + declaration + $$"""

            static class Registration
            {
                static void Register<T>(Pixely.DependencyInjection.ServiceCollection services) => services.AddSystem<{{system}}>();
            }
            """;

        (ImmutableArray<Diagnostic> diagnostics, _) = Run(source);

        Assert.That(diagnostics.Select(d => d.Id), Is.EqualTo(new[] { expectedId }));
    }

    // AddSystem's constraints reject a struct, or a class that is not an ISystem, before the generator sees the call.
    [TestCase("struct S : ISystem { public void Update() { } }", "CS0452")]
    [TestCase("class S { }", "CS0311")]
    public void SystemBreakingTheConstraintsIsRejectedByTheCompiler(string declaration, string expectedId)
    {
        string source = Components + declaration + """

            static class Registration
            {
                static void Register(Pixely.DependencyInjection.ServiceCollection services) => services.AddSystem<S>();
            }
            """;

        (ImmutableArray<Diagnostic> generatorDiagnostics, Compilation output) = Run(source);

        Assert.That(generatorDiagnostics, Is.Empty);
        Assert.That(output.GetDiagnostics().Select(d => d.Id), Does.Contain(expectedId));
    }

    [TestCase("public struct T : ITag { public int X; }")]
    [TestCase("public struct T : ITag { public int X { get; set; } }")]
    [TestCase("public record struct T(int X) : ITag;")]
    [TestCase("public struct T(int x) : ITag { public int X => x; }")]
    [TestCase("public partial struct T : ITag; public partial struct T : ITag { public int X; }")]
    public void TagWithDataReportsDiagnostic(string declaration)
    {
        (ImmutableArray<Diagnostic> diagnostics, _) = Run(Components + declaration);

        Assert.That(diagnostics.Select(d => d.Id), Is.EqualTo(new[] { "PXECS013" }));
    }

    // A tag has no column, so a spawner takes no value for the tags of its IWith<...>.
    [Test]
    public void TagsOfASpawnerCompile()
    {
        string source = Components + """
            public struct Marked : ITag;

            public readonly record struct Flagged : ITag
            {
                public static int Count => 0;
            }

            public sealed class Flags : Spawner<Position>, IWith<Marked, Flagged>;

            public sealed class S(Flags flags) : ISystem
            {
                public void Update() => flags.Spawn(new Position());

                static void Register(Pixely.DependencyInjection.ServiceCollection services) => services.AddSystem<S>();
            }
            """;

        (ImmutableArray<Diagnostic> generatorDiagnostics, Compilation output) = Run(source);

        Assert.That(generatorDiagnostics, Is.Empty);
        Assert.That(output.GetDiagnostics().Where(d => d.Severity >= DiagnosticSeverity.Warning), Is.Empty);
    }

    [Test]
    public void TypeParameterSystemReportsDiagnostic()
    {
        string source = Components + """
            static class Registration
            {
                static void Register<T>(Pixely.DependencyInjection.ServiceCollection services) where T : class, ISystem => services.AddSystem<T>();
            }
            """;

        (ImmutableArray<Diagnostic> diagnostics, _) = Run(source);

        Assert.That(diagnostics.Select(d => d.Id), Is.EqualTo(new[] { "PXECS010" }));
    }

    [Test]
    public void EveryAddSystemCallGetsItsOwnInterceptor()
    {
        string source = Components + """
            public class Outer<T>
            {
                public sealed class Nested : ISystem
                {
                    public void Update()
                    {
                    }
                }
            }

            public sealed class First : ISystem
            {
                public void Update()
                {
                }
            }

            static class Registration
            {
                static void Register(Pixely.DependencyInjection.ServiceCollection services)
                {
                    services.AddEcs();
                    services.AddSystem<First>();
                    services.AddSystem<Outer<int>.Nested>();
                    services.AddSystem<Outer<dynamic>.Nested>();
                }
            }
            """;

        (ImmutableArray<Diagnostic> generatorDiagnostics, Compilation output) = Run(source);

        Assert.That(generatorDiagnostics, Is.Empty);
        Assert.That(output.GetDiagnostics().Where(d => d.Severity >= DiagnosticSeverity.Warning), Is.Empty);
        string interceptors = output.SyntaxTrees.Single(tree => tree.FilePath.EndsWith("SystemInterceptors.g.cs")).ToString();
        Assert.That(interceptors, Does.Contain("AddSystem_0").And.Contain("AddSystem_1").And.Contain("AddSystem_2"));
        Assert.That(interceptors, Does.Contain("new global::First(").And.Contain("new global::Outer<int>.Nested(").And.Contain("new global::Outer<dynamic>.Nested("));
    }

    [Test]
    public void AnotherMethodNamedAddSystemIsNotIntercepted()
    {
        string source = Components + """
            public sealed class S : ISystem
            {
                public void Update()
                {
                }
            }

            static class Other
            {
                public static void AddSystem<T>(this Pixely.DependencyInjection.ServiceCollection services)
                {
                }

                static void Register(Pixely.DependencyInjection.ServiceCollection services) => services.AddSystem<S>();
            }
            """;

        (ImmutableArray<Diagnostic> generatorDiagnostics, Compilation output) = Run(source);

        Assert.That(generatorDiagnostics, Is.Empty);
        Assert.That(output.GetDiagnostics().Where(d => d.Severity >= DiagnosticSeverity.Warning), Is.Empty);
        Assert.That(output.SyntaxTrees.Any(tree => tree.FilePath.EndsWith("SystemInterceptors.g.cs")), Is.False);
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
            public static class @class
            {
                public sealed class @event : Query<ReadOnlySpan<Entity>, Span<Position>>;

                public sealed class @struct : Spawner<Position, Velocity>;

                public sealed class @record : Spawner<Position>;

                public sealed class @operator(@event query, @struct structs, @record records) : ISystem
                {
                    public void Update()
                    {
                        foreach ((ReadOnlySpan<Entity> @this, Span<Position> @base) in query)
                        {
                            @base[0] = @base[0];
                            structs.Spawn(@base[0], default);
                            records.Spawn(@base[0]);
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

    // SystemScheduler.Add takes a system, or a function that creates one through the builder, so it needs no generated code.
    [Test]
    public void SchedulerAddIsNotIntercepted()
    {
        string source = Components + """
            public sealed class Q : Query<ReadOnlySpan<Position>>;

            public sealed class S : ISystem
            {
                public void Update()
                {
                }
            }

            public sealed class Spawning(Spawner<Position> spawner, Q query) : ISystem
            {
                public void Update()
                {
                    spawner.Spawn(new Position());
                    foreach (Columns<ReadOnlySpan<Position>> columns in query)
                    {
                    }
                }
            }

            static class Registration
            {
                static void Register(SystemScheduler scheduler)
                {
                    scheduler.Add(new S());
                    scheduler.Add(builder => new Spawning(builder.Spawner<Spawner<Position>>(), builder.Query<Q>()));
                }

                static void Register<T>(SystemScheduler scheduler, T system) where T : class, ISystem => scheduler.Add(system);
            }
            """;

        (ImmutableArray<Diagnostic> generatorDiagnostics, Compilation output) = Run(source);

        Assert.That(generatorDiagnostics, Is.Empty);
        Assert.That(output.GetDiagnostics().Where(d => d.Severity >= DiagnosticSeverity.Warning), Is.Empty);
        Assert.That(output.SyntaxTrees.Any(tree => tree.FilePath.EndsWith("SystemInterceptors.g.cs")), Is.False);
    }

    // A method group is not a call, so there is nothing to intercept.
    [Test]
    public void MethodGroupsOfAddSystemReportDiagnostic()
    {
        string source = Components + """
            public sealed class S(EntityDestroyer destroyer) : ISystem
            {
                public void Update()
                {
                }
            }

            static class Registration
            {
                static void Register(Pixely.DependencyInjection.ServiceCollection services)
                {
                    System.Action add = services.AddSystem<S>;
                    System.Action<Pixely.DependencyInjection.ServiceCollection> addStatic = EcsServiceCollectionExtensions.AddSystem<S>;
                }
            }
            """;

        (ImmutableArray<Diagnostic> diagnostics, _) = Run(source);

        Assert.That(diagnostics.Select(d => d.Id), Is.EqualTo(new[] { "PXECS015", "PXECS015" }));
    }

    // SystemScheduler.Add needs no generated code, and nameof calls nothing.
    [Test]
    public void MethodGroupOfAddAndNameofReportNothing()
    {
        string source = Components + """
            public sealed class P : ISystem
            {
                public void Update()
                {
                }
            }

            static class Registration
            {
                static string Register(SystemScheduler scheduler)
                {
                    System.Func<P, P> schedule = scheduler.Add;
                    return nameof(scheduler.Add) + nameof(EcsServiceCollectionExtensions.AddSystem);
                }
            }
            """;

        (ImmutableArray<Diagnostic> generatorDiagnostics, Compilation output) = Run(source);

        Assert.That(generatorDiagnostics, Is.Empty);
        Assert.That(output.GetDiagnostics().Where(d => d.Severity >= DiagnosticSeverity.Warning), Is.Empty);
    }

    [Test]
    public void CallThroughConditionalAccessIsIntercepted()
    {
        string source = Components + """
            public sealed class S : ISystem
            {
                public S(EntityDestroyer destroyer)
                {
                }

                public void Update()
                {
                }
            }

            static class Registration
            {
                static void Register(Pixely.DependencyInjection.ServiceCollection? services) => services?.AddSystem<S>();
            }
            """;

        (ImmutableArray<Diagnostic> generatorDiagnostics, Compilation output) = Run(source);

        Assert.That(generatorDiagnostics, Is.Empty);
        Assert.That(output.GetDiagnostics().Where(d => d.Severity >= DiagnosticSeverity.Warning), Is.Empty);
        string interceptors = output.SyntaxTrees.Single(tree => tree.FilePath.EndsWith("SystemInterceptors.g.cs")).ToString();
        Assert.That(interceptors, Does.Contain("AddSystem_0").And.Contain("builder.EntityDestroyer()"));
    }

    [TestCase("class S : ISystem, Pixely.IUpdatable { public void Update() { } public int UpdateOrder => 0; void Pixely.IUpdatable.Update() { } }")]
    [TestCase("class S(EntityDestroyer d) : ISystem, Pixely.IUpdatable { public void Update() { } }")]
    public void SystemThatIsAlsoAnIUpdatableReportsDiagnostic(string declaration)
    {
        string source = Components + declaration + """

            static class Registration
            {
                static void Register(Pixely.DependencyInjection.ServiceCollection services) => services.AddSystem<S>();
            }
            """;

        (ImmutableArray<Diagnostic> diagnostics, _) = Run(source);

        Assert.That(diagnostics.Select(d => d.Id), Is.EqualTo(new[] { "PXECS014" }));
    }

    // AddSystem reserves the system's order when it is called, because the provider does not create singletons in call order.
    [Test]
    public void AddSystemReservesItsOrderWhenCalled()
    {
        string source = Components + """
            public sealed class S : ISystem
            {
                public void Update()
                {
                }
            }

            static class Registration
            {
                static void Register(Pixely.DependencyInjection.ServiceCollection services) => services.AddSystem<S>();
            }
            """;

        (_, Compilation output) = Run(source);

        string interceptors = output.SyntaxTrees.Single(tree => tree.FilePath.EndsWith("SystemInterceptors.g.cs")).ToString();
        Assert.That(interceptors, Does.Contain("long order = global::Pixely.Ecs.SystemScheduler.ReserveOrder();").And.Contain(", order));"));
    }

    // A query whose Query<...> comes through a generic base is registered through its declaration, or through the closed type that AddQuery names
    // or that the constructor of a system added with AddSystem takes.
    [Test]
    public void SpansBehindAGenericBaseAreRegistered()
    {
        string source = Components + """
            public record struct Mass(float Value);
            public record struct Speed(float Value);
            public record struct Heat(float Value);

            abstract class ComponentQuery<T> : Query<ReadOnlySpan<T>> where T : struct;

            sealed class Masses : ComponentQuery<Mass>;

            sealed class Of<T> : Query<Span<T>> where T : struct;

            sealed class S : ISystem
            {
                public S(Of<Speed> speeds)
                {
                }

                public void Update()
                {
                }
            }

            static class Reader
            {
                static void Read(Pixely.DependencyInjection.ServiceCollection services) => services.AddQuery<Of<Heat>>();

                static void Register(Pixely.DependencyInjection.ServiceCollection services) => services.AddSystem<S>();
            }
            """;

        (ImmutableArray<Diagnostic> generatorDiagnostics, Compilation output) = Run(source);

        Assert.That(generatorDiagnostics, Is.Empty);
        Assert.That(output.GetDiagnostics().Where(d => d.Severity >= DiagnosticSeverity.Warning), Is.Empty);
        string registration = output.SyntaxTrees.Single(tree => tree.FilePath.EndsWith("QueryTerms.g.cs")).ToString();
        Assert.That(registration, Does.Contain("RegisterReadOnlySpan<global::Mass>();").And.Contain("RegisterSpan<global::Speed>();").And.Contain("RegisterSpan<global::Heat>();"));
    }

    // The constructor is read from the type AddSystem names, so a record system and the type parameters of a generic system count too.
    [Test]
    public void QueriesOfRecordAndGenericSystemsAreRegistered()
    {
        string source = Components + """
            public record struct Charge(float Value);
            public record struct Light(float Value);
            public record struct Spin(float Value);

            sealed class Of<T> : Query<Span<T>> where T : struct;

            sealed record Recorded(Of<Charge> Charges) : ISystem
            {
                public void Update()
                {
                }
            }

            sealed class Generic<T>(Of<T> items, Query<ReadOnlySpan<T>> reads) : ISystem where T : struct
            {
                public void Update()
                {
                    foreach (Columns<Span<T>> columns in items)
                    {
                    }

                    foreach (Columns<ReadOnlySpan<T>> columns in reads)
                    {
                    }
                }
            }

            class Outer<T> where T : struct
            {
                public sealed class Inner : ISystem
                {
                    public Inner(Of<T> items)
                    {
                    }

                    public void Update()
                    {
                    }
                }
            }

            static class Registration
            {
                static void Register(Pixely.DependencyInjection.ServiceCollection services)
                {
                    services.AddSystem<Recorded>();
                    services.AddSystem<Generic<Light>>();
                    services.AddSystem<Outer<Spin>.Inner>();
                }
            }
            """;

        (ImmutableArray<Diagnostic> generatorDiagnostics, Compilation output) = Run(source);

        Assert.That(generatorDiagnostics, Is.Empty);
        Assert.That(output.GetDiagnostics().Where(d => d.Severity >= DiagnosticSeverity.Warning), Is.Empty);
        string registration = output.SyntaxTrees.Single(tree => tree.FilePath.EndsWith("QueryTerms.g.cs")).ToString();
        Assert.That(registration, Does.Contain("RegisterSpan<global::Charge>();").And.Contain("RegisterSpan<global::Light>();")
            .And.Contain("RegisterReadOnlySpan<global::Light>();").And.Contain("RegisterSpan<global::Spin>();"));
    }

    // A span closed with a system's type argument is checked where AddSystem names it. The query then reads only a tag, which also leaves it without
    // a required component.
    [Test]
    public void InvalidSpanClosedByAGenericSystemReportsDiagnostic()
    {
        string source = Components + """
            public struct Mark : ITag;

            sealed class Generic<T> : ISystem where T : struct
            {
                public Generic(Query<ReadOnlySpan<T>> reads)
                {
                }

                public void Update()
                {
                }
            }

            static class Registration
            {
                static void Register(Pixely.DependencyInjection.ServiceCollection services) => services.AddSystem<Generic<Mark>>();
            }
            """;

        (ImmutableArray<Diagnostic> diagnostics, _) = Run(source);

        Assert.That(diagnostics.Select(d => d.Id), Is.EquivalentTo(new[] { "PXECS002", "PXECS004" }));
    }

    // Only the constructor the generated code calls counts, and nameof adds no system.
    [Test]
    public void QueriesOfAnUncalledConstructorOrANameofAreNotChecked()
    {
        string source = Components + """
            public struct Mark : ITag;

            sealed class Of<T> : Query<Span<T>> where T : struct;

            sealed class S : ISystem
            {
                public S(Of<Position> positions)
                {
                }

                private S(Of<Mark> marks)
                {
                }

                public void Update()
                {
                }
            }

            sealed class Generic<T> : ISystem where T : struct
            {
                public Generic(Query<ReadOnlySpan<T>> reads)
                {
                }

                public void Update()
                {
                }
            }

            static class Registration
            {
                static void Register(Pixely.DependencyInjection.ServiceCollection services) => services.AddSystem<S>();

                static string Name() => nameof(EcsServiceCollectionExtensions.AddSystem<Generic<Mark>>);
            }
            """;

        (ImmutableArray<Diagnostic> diagnostics, Compilation output) = Run(source);

        Assert.That(diagnostics, Is.Empty);
        string registration = output.SyntaxTrees.Single(tree => tree.FilePath.EndsWith("QueryTerms.g.cs")).ToString();
        Assert.That(registration, Does.Contain("RegisterSpan<global::Position>();").And.Not.Contain("global::Mark"));
    }

    // The open query already reports its invalid type argument where it is written, so AddSystem and AddQuery do not report it again.
    [TestCase("Query<Span<T>, int>", "")]
    [TestCase("Of<T>", "sealed class Of<T> : Query<Span<T>, int> where T : struct; static class Reader { static void Read(Pixely.DependencyInjection.ServiceCollection services) => services.AddQuery<Of<Velocity>>(); }")]
    [TestCase("Of<T>", "class Of<T> : Query<Span<T>, int> where T : struct; sealed class Positions : Of<Position>;")]
    public void InvalidSpanOfAnOpenQueryIsReportedOnce(string queryType, string declarations)
    {
        string source = Components + declarations + $$"""

            sealed class Generic<T> : ISystem where T : struct
            {
                public Generic({{queryType}} query)
                {
                }

                public void Update()
                {
                }
            }

            static class Registration
            {
                static void Register(Pixely.DependencyInjection.ServiceCollection services) => services.AddSystem<Generic<Position>>();
            }
            """;

        (ImmutableArray<Diagnostic> diagnostics, _) = Run(source);

        Assert.That(diagnostics.Select(d => d.Id), Is.EqualTo(new[] { "PXECS002" }));
    }

    // A referenced assembly is not scanned, so its query class reported nothing, and the closed query is reported where it is named.
    [Test]
    public void InvalidSpanOfAQueryFromAReferencedAssemblyIsReported()
    {
        CSharpCompilation external = CSharpCompilation.Create("External",
            [CSharpSyntaxTree.ParseText("using System; using Pixely.Ecs; namespace External { public sealed class Of<T> : Query<Span<T>, int> where T : struct; }", ParseOptions)],
            CreateReferences(), new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using MemoryStream image = new MemoryStream();
        Assert.That(external.Emit(image).Success, Is.True);
        CSharpCompilation compilation = CreateCompilation(Components + "static class Reader { static void Read(Pixely.DependencyInjection.ServiceCollection services) => services.AddQuery<External.Of<Position>>(); }")
            .AddReferences(MetadataReference.CreateFromImage(image.ToArray()));

        CreateDriver().RunGeneratorsAndUpdateCompilation(compilation, out _, out ImmutableArray<Diagnostic> diagnostics);

        Assert.That(diagnostics.Select(d => d.Id), Is.EqualTo(new[] { "PXECS002" }));
    }

    [TestCase("public struct T : ITag; abstract class ComponentQuery<C> : Query<ReadOnlySpan<C>> where C : struct; sealed class Q : ComponentQuery<T>;")]
    [TestCase("sealed class Of<C> : Query<Span<C>> where C : struct; static class R { static void Read(Pixely.DependencyInjection.ServiceCollection services) => services.AddQuery<Of<Entity>>(); }")]
    public void InvalidSpanBehindAGenericBaseReportsDiagnostic(string declaration)
    {
        (ImmutableArray<Diagnostic> diagnostics, _) = Run(Components + declaration);

        Assert.That(diagnostics.Select(d => d.Id), Is.EqualTo(new[] { "PXECS002" }));
    }

    [Test]
    public void QualifiedAndAliasedGenericQueriesAreRegistered()
    {
        string source = """
            using HeatQuery = Game.Of<Heat>;
            using MassBase = Game.ComponentQuery<Mass>;

            """ + Components + """
            public record struct Mass(float Value);
            public record struct Speed(float Value);
            public record struct Heat(float Value);
            public record struct Light(float Value);

            namespace Game
            {
                public sealed class Of<T> : Query<Span<T>> where T : struct;

                public abstract class ComponentQuery<T> : Query<ReadOnlySpan<T>> where T : struct;
            }

            sealed class Masses : MassBase;

            sealed class S : ISystem
            {
                public S(Game.Of<Speed> speeds, HeatQuery heats)
                {
                }

                public void Update()
                {
                }
            }

            static class Reader
            {
                static void Read(Pixely.DependencyInjection.ServiceCollection services) => services.AddQuery<Game.Of<Light>>();

                static void Register(Pixely.DependencyInjection.ServiceCollection services) => services.AddSystem<S>();
            }
            """;

        (ImmutableArray<Diagnostic> generatorDiagnostics, Compilation output) = Run(source);

        Assert.That(generatorDiagnostics, Is.Empty);
        Assert.That(output.GetDiagnostics().Where(d => d.Severity >= DiagnosticSeverity.Warning), Is.Empty);
        string registration = output.SyntaxTrees.Single(tree => tree.FilePath.EndsWith("QueryTerms.g.cs")).ToString();
        Assert.That(registration, Does.Contain("RegisterReadOnlySpan<global::Mass>();").And.Contain("RegisterSpan<global::Speed>();")
            .And.Contain("RegisterSpan<global::Heat>();").And.Contain("RegisterSpan<global::Light>();"));
    }

    // The derived query is in another file, which the edit leaves unchanged, so a cached result for it would keep the old registration.
    [Test]
    public void EditingAGenericBaseInAnotherFileUpdatesTheRegistration()
    {
        string derived = Components + """
            public record struct Mass(float Value);

            sealed class Masses : ComponentQuery<Mass>;
            """;
        string readOnlyBase = "using System; using Pixely.Ecs; abstract class ComponentQuery<T> : Query<ReadOnlySpan<T>> where T : struct;";
        string writableBase = "using System; using Pixely.Ecs; abstract class ComponentQuery<T> : Query<Span<T>> where T : struct;";

        string registration = RunEdit(derived, readOnlyBase, writableBase, out ImmutableArray<Diagnostic> diagnostics, out string before);

        Assert.That(diagnostics, Is.Empty);
        Assert.That(before, Does.Contain("RegisterReadOnlySpan<global::Mass>();"));
        Assert.That(registration, Does.Contain("RegisterSpan<global::Mass>();").And.Not.Contain("RegisterReadOnlySpan<global::Mass>();"));
    }

    [Test]
    public void AddingIUpdatableInAnotherFileReportsDiagnostic()
    {
        string registration = Components + """
            static class Registration
            {
                static void Register(Pixely.DependencyInjection.ServiceCollection services) => services.AddSystem<S>();
            }
            """;
        string plain = "using Pixely.Ecs; public sealed class S : ISystem { public void Update() { } }";
        string updatable = "using Pixely.Ecs; public sealed class S : ISystem, Pixely.IUpdatable { public void Update() { } public int UpdateOrder => 0; void Pixely.IUpdatable.Update() { } }";

        RunEdit(registration, plain, updatable, out ImmutableArray<Diagnostic> diagnostics, out _);

        Assert.That(diagnostics.Select(d => d.Id), Is.EqualTo(new[] { "PXECS014" }));
    }

    private static (ImmutableArray<Diagnostic> GeneratorDiagnostics, Compilation Output) Run(string source)
    {
        CSharpCompilation compilation = CreateCompilation(source);
        CreateDriver().RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out ImmutableArray<Diagnostic> diagnostics);
        return (diagnostics, output);
    }

    // Runs one driver on the unchanged file and the edited file before and after the edit, and returns the query registration after it.
    private static string RunEdit(string unchanged, string editedBefore, string editedAfter, out ImmutableArray<Diagnostic> diagnostics, out string registrationBefore)
    {
        CSharpCompilation before = CreateCompilation(unchanged, editedBefore);
        GeneratorDriver driver = CreateDriver().RunGeneratorsAndUpdateCompilation(before, out Compilation outputBefore, out _);
        registrationBefore = GetRegistration(outputBefore);

        SyntaxTree editedTree = before.SyntaxTrees.Last();
        CSharpCompilation after = before.ReplaceSyntaxTree(editedTree, CSharpSyntaxTree.ParseText(editedAfter, ParseOptions, editedTree.FilePath));
        driver.RunGeneratorsAndUpdateCompilation(after, out Compilation outputAfter, out diagnostics);
        return GetRegistration(outputAfter);
    }

    private static string GetRegistration(Compilation output) => output.SyntaxTrees.SingleOrDefault(tree => tree.FilePath.EndsWith("QueryTerms.g.cs"))?.ToString() ?? "";

    private static readonly CSharpParseOptions ParseOptions = new CSharpParseOptions(LanguageVersion.Preview).WithFeatures([new KeyValuePair<string, string>("InterceptorsNamespaces", "Pixely.Ecs.Generated")]);

    private static CSharpCompilation CreateCompilation(params string[] sources)
    {
        return CSharpCompilation.Create(
            "TestAssembly",
            sources.Select((source, index) => CSharpSyntaxTree.ParseText(source, ParseOptions, $"Source{index}.cs")),
            CreateReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary).WithNullableContextOptions(NullableContextOptions.Enable));
    }

    private static IEnumerable<MetadataReference> CreateReferences()
    {
        string[] platformAssemblies = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
        return platformAssemblies
            .Append(typeof(World).Assembly.Location)
            .Append(typeof(Pixely.DependencyInjection.ServiceCollection).Assembly.Location)
            .Append(typeof(IUpdatable).Assembly.Location)
            .Select(path => MetadataReference.CreateFromFile(path));
    }

    private static GeneratorDriver CreateDriver() =>
        CSharpGeneratorDriver.Create([new QueryTermGenerator().AsSourceGenerator(), new SystemGenerator().AsSourceGenerator(), new TagGenerator().AsSourceGenerator()],
            parseOptions: ParseOptions);
}
