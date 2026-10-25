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

                internal sealed class Mover : ISystem<Movement, Queries.Corpses, Query<ReadOnlySpan<Velocity>>>
                {
                    public void Update(Movement movement, Queries.Corpses corpses, Query<ReadOnlySpan<Velocity>> velocities)
                    {
                        foreach ((ReadOnlySpan<Entity> entities, Span<Position> positions, ReadOnlySpan<Velocity> speeds) in movement)
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
                    public static void Register(ServiceCollection services, SystemScheduler scheduler)
                    {
                        services.AddEcs();
                        services.AddSystem<Mover>();
                        scheduler.Add(new Mover());
                    }
                }
            }
            """;

        (ImmutableArray<Diagnostic> generatorDiagnostics, Compilation output) = Run(source);

        Assert.That(generatorDiagnostics, Is.Empty);
        Assert.That(output.GetDiagnostics().Where(d => d.Severity >= DiagnosticSeverity.Warning), Is.Empty);
        string interceptors = output.SyntaxTrees.Single(tree => tree.FilePath.EndsWith("SystemInterceptors.g.cs")).ToString();
        Assert.That(interceptors, Does.Contain("AddSystem_0").And.Contain("Add_1"));
        Assert.That(interceptors, Does.Contain("builder.Query<global::Game.Queries.Corpses>()").And.Contain("builder.Query<global::Pixely.Ecs.Query<global::System.ReadOnlySpan<global::Velocity>>>()"));
    }

    [TestCase("abstract class Q : Query<ReadOnlySpan<Position>>;", "PXECS001")]
    [TestCase("sealed class Q : Query<ReadOnlySpan<Position>> { public Q(int value) { } }", "PXECS001")]
    [TestCase("sealed class Q : Query<ReadOnlySpan<Position>> { internal Q() { } }", "PXECS001")]
    [TestCase("sealed class Q : Query<ReadOnlySpan<Entity>>;", "PXECS004")]
    [TestCase("sealed class Q : Query<ReadOnlySpan<Entity>>, IWithout<Position>;", "PXECS004")]
    [TestCase("sealed class Q : Query<ReadOnlySpan<Position>>, IWithout<Position>;", "PXECS005")]
    [TestCase("sealed class Q : Query<ReadOnlySpan<Entity>>, IWith<Frozen>, IWithout<Frozen>;", "PXECS005")]
    public void InvalidQueryOfASystemReportsDiagnostic(string declaration, string expectedId)
    {
        string source = Components + declaration + """

            sealed class S : ISystem<Q>
            {
                public void Update(Q query)
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
    [TestCase("sealed class Q : Query<ReadOnlySpan<Position>, Span<Position>>;", "PXECS003")]
    [TestCase("static class R { static void Read(World world) => world.Query<Query<Span<Entity>>>(); }", "PXECS002")]
    [TestCase("static class R { static void Read(World world) => world.Query<Query<ReadOnlySpan<Position>, ReadOnlySpan<Position>>>(); }", "PXECS003")]
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

            sealed class S : ISystem<Query<Span<Velocity>>>
            {
                public void Update(Query<Span<Velocity>> velocities)
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
            "global::Pixely.Ecs.QueryTerms.RegisterSpan<global::Velocity>();"
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

                internal sealed class Shooter : ISystem<Movement, Bullets, ComponentAdder<Dead>, ComponentRemover<Velocity>, EntityDestroyer, Spawners.Corpses, Spawner<Velocity>>
                {
                    public Shooter(Log log, Log? optional, IEnumerable<Log> logs, in Log byValue, IReadOnlyList<Log> list)
                    {
                    }

                    public void Update(Movement movement, Bullets bullets, ComponentAdder<Dead> dead, ComponentRemover<Velocity> stop, EntityDestroyer destroyer,
                        Spawners.Corpses corpses, Spawner<Velocity> velocities)
                    {
                        bullets.Spawn(new Position());
                        corpses.Spawn(new Position());
                        velocities.Spawn(new Velocity());
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

            sealed class S : ISystem<P>
            {
                public void Update(P spawner)
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
    [TestCase("class S : ISystem { public S(ComponentAdder<Position> adder) { } public void Update() { } }", "S", "PXECS013")]
    [TestCase("sealed class Q : Query<ReadOnlySpan<Position>>; class S : ISystem { public S(Q query) { } public void Update() { } }", "S", "PXECS013")]
    [TestCase("sealed class P : Spawner<Position>; class S : ISystem { public S(P spawner) { } public void Update() { } }", "S", "PXECS013")]
    [TestCase("class S { }", "S", "PXECS015")]
    [TestCase("class S : ISystem, ISystem<EntityDestroyer> { public void Update() { } public void Update(EntityDestroyer d) { } }", "S", "PXECS015")]
    [TestCase("class S : ISystem<EntityDestroyer>, ISystem<ComponentAdder<Position>> { public void Update(EntityDestroyer d) { } public void Update(ComponentAdder<Position> a) { } }", "S", "PXECS015")]
    [TestCase("class Log; class S : ISystem<Log> { public void Update(Log log) { } }", "S", "PXECS016")]
    [TestCase("class S : ISystem<World> { public void Update(World world) { } }", "S", "PXECS016")]
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

    // AddSystem's class constraint rejects a struct before the generator sees the call.
    [Test]
    public void StructSystemIsRejectedByTheCompiler()
    {
        string source = Components + """
            struct S : ISystem
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

        (ImmutableArray<Diagnostic> generatorDiagnostics, Compilation output) = Run(source);

        Assert.That(generatorDiagnostics, Is.Empty);
        Assert.That(output.GetDiagnostics().Select(d => d.Id), Does.Contain("CS0452"));
    }

    [TestCase("public struct T : ITag { public int X; }")]
    [TestCase("public struct T : ITag { public int X { get; set; } }")]
    [TestCase("public record struct T(int X) : ITag;")]
    [TestCase("public struct T(int x) : ITag { public int X => x; }")]
    [TestCase("public partial struct T : ITag; public partial struct T : ITag { public int X; }")]
    public void TagWithDataReportsDiagnostic(string declaration)
    {
        (ImmutableArray<Diagnostic> diagnostics, _) = Run(Components + declaration);

        Assert.That(diagnostics.Select(d => d.Id), Is.EqualTo(new[] { "PXECS014" }));
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

            public sealed class S : ISystem<Flags>
            {
                public void Update(Flags flags) => flags.Spawn(new Position());

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
            public static partial class @class
            {
                public sealed class @event : Query<ReadOnlySpan<Entity>, Span<Position>>;

                public sealed class @struct : Spawner<Position, Velocity>;

                public sealed class @record : Spawner<Position>;

                public sealed class @operator : ISystem<@event, @struct, @record>
                {
                    public void Update(@event query, @struct structs, @record records)
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

    [Test]
    public void SchedulerAddOfAGenericSystemWithASpawnerIsIntercepted()
    {
        string source = Components + """
            public sealed class Generic<T>(System.Action<T> update) : ISystem<T>
            {
                public void Update(T item1) => update(item1);
            }

            static class Registration
            {
                static void Register(SystemScheduler scheduler) => scheduler.Add(new Generic<Spawner<Position>>(spawner => spawner.Spawn(new Position())));
            }
            """;

        (ImmutableArray<Diagnostic> generatorDiagnostics, Compilation output) = Run(source);

        Assert.That(generatorDiagnostics, Is.Empty);
        Assert.That(output.GetDiagnostics().Where(d => d.Severity >= DiagnosticSeverity.Warning), Is.Empty);
        Assert.That(output.SyntaxTrees.Any(tree => tree.ToString().Contains("builder.Spawner<global::Pixely.Ecs.Spawner<global::Position>>()")), Is.True);
    }

    // SystemScheduler.Add handles an ISystem at runtime, and a type parameter is known only where the generic code is called.
    [Test]
    public void SchedulerAddOfAnISystemOrATypeParameterIsNotIntercepted()
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
                static void Register(SystemScheduler scheduler) => scheduler.Add(new S());

                static void Register<T>(SystemScheduler scheduler, T system) where T : class => scheduler.Add(system);

                static void RegisterSystem<T>(SystemScheduler scheduler, T system) where T : class, ISystem => scheduler.Add(system);
            }
            """;

        (ImmutableArray<Diagnostic> generatorDiagnostics, Compilation output) = Run(source);

        Assert.That(generatorDiagnostics, Is.Empty);
        Assert.That(output.GetDiagnostics().Where(d => d.Severity >= DiagnosticSeverity.Warning), Is.Empty);
        Assert.That(output.SyntaxTrees.Any(tree => tree.FilePath.EndsWith("SystemInterceptors.g.cs")), Is.False);
    }

    // Add of a type parameter is intercepted only where the generic code is called, so a constraint to an ISystem<...> means it always throws.
    [Test]
    public void SchedulerAddOfATypeParameterConstrainedToAnISystemWithTypeArgumentsReportsDiagnostic()
    {
        string source = Components + """
            public sealed class Q : Query<ReadOnlySpan<Position>>;

            public abstract class Base : ISystem<Q>
            {
                public abstract void Update(Q query);
            }

            static class Registration
            {
                static void Register<T>(SystemScheduler scheduler, T system) where T : class, ISystem<Q> => scheduler.Add(system);

                static void RegisterBase<T>(SystemScheduler scheduler, T system) where T : Base => scheduler.Add(system);

                static void RegisterNested<T, U>(SystemScheduler scheduler, T system) where T : class, U where U : ISystem<Q> => scheduler.Add(system);
            }
            """;

        (ImmutableArray<Diagnostic> diagnostics, _) = Run(source);

        Assert.That(diagnostics.Select(d => d.Id), Is.EqualTo(new[] { "PXECS010", "PXECS010", "PXECS010" }));
    }

    // A method group is not a call, so there is nothing to intercept.
    [Test]
    public void MethodGroupsOfAddSystemAndAddReportDiagnostic()
    {
        string source = Components + """
            public sealed class S : ISystem<EntityDestroyer>
            {
                public void Update(EntityDestroyer destroyer)
                {
                }
            }

            static class Registration
            {
                static void Register(Pixely.DependencyInjection.ServiceCollection services, SystemScheduler scheduler)
                {
                    System.Action add = services.AddSystem<S>;
                    System.Action<Pixely.DependencyInjection.ServiceCollection> addStatic = EcsServiceCollectionExtensions.AddSystem<S>;
                    System.Func<S, S> schedule = scheduler.Add;
                }
            }
            """;

        (ImmutableArray<Diagnostic> diagnostics, _) = Run(source);

        Assert.That(diagnostics.Select(d => d.Id), Is.EqualTo(new[] { "PXECS018", "PXECS018", "PXECS018" }));
    }

    // SystemScheduler.Add handles an ISystem at runtime, and nameof calls nothing.
    [Test]
    public void MethodGroupOfAddForAnISystemAndNameofReportNothing()
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
    public void CallsThroughConditionalAccessAreIntercepted()
    {
        string source = Components + """
            public sealed class S : ISystem<EntityDestroyer>
            {
                public void Update(EntityDestroyer destroyer)
                {
                }
            }

            static class Registration
            {
                static void Register(Pixely.DependencyInjection.ServiceCollection? services, SystemScheduler? scheduler)
                {
                    services?.AddSystem<S>();
                    scheduler?.Add(new S());
                }
            }
            """;

        (ImmutableArray<Diagnostic> generatorDiagnostics, Compilation output) = Run(source);

        Assert.That(generatorDiagnostics, Is.Empty);
        Assert.That(output.GetDiagnostics().Where(d => d.Severity >= DiagnosticSeverity.Warning), Is.Empty);
        string interceptors = output.SyntaxTrees.Single(tree => tree.FilePath.EndsWith("SystemInterceptors.g.cs")).ToString();
        Assert.That(interceptors, Does.Contain("AddSystem_0").And.Contain("Add_1"));
    }

    [TestCase("class S : ISystem, Pixely.IUpdatable { public void Update() { } public int UpdateOrder => 0; void Pixely.IUpdatable.Update() { } }")]
    [TestCase("class S : ISystem<EntityDestroyer>, Pixely.IUpdatable { public void Update(EntityDestroyer d) { } public void Update() { } }")]
    public void SystemThatIsAlsoAnIUpdatableReportsDiagnostic(string declaration)
    {
        string source = Components + declaration + """

            static class Registration
            {
                static void Register(Pixely.DependencyInjection.ServiceCollection services, SystemScheduler scheduler)
                {
                    services.AddSystem<S>();
                    scheduler.Add(new S());
                }
            }
            """;

        (ImmutableArray<Diagnostic> diagnostics, _) = Run(source);

        Assert.That(diagnostics.Select(d => d.Id), Is.EqualTo(new[] { "PXECS017", "PXECS017" }));
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

    // A query whose Query<...> comes through a generic base is registered through its declaration, or through the closed type a system or World.Query names.
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

            sealed class S : ISystem<Of<Speed>>
            {
                public void Update(Of<Speed> speeds)
                {
                }
            }

            static class Reader
            {
                static void Read(World world) => world.Query<Of<Heat>>();
            }
            """;

        (ImmutableArray<Diagnostic> generatorDiagnostics, Compilation output) = Run(source);

        Assert.That(generatorDiagnostics, Is.Empty);
        Assert.That(output.GetDiagnostics().Where(d => d.Severity >= DiagnosticSeverity.Warning), Is.Empty);
        string registration = output.SyntaxTrees.Single(tree => tree.FilePath.EndsWith("QueryTerms.g.cs")).ToString();
        Assert.That(registration, Does.Contain("RegisterReadOnlySpan<global::Mass>();").And.Contain("RegisterSpan<global::Speed>();").And.Contain("RegisterSpan<global::Heat>();"));
    }

    [TestCase("public struct T : ITag; abstract class ComponentQuery<C> : Query<ReadOnlySpan<C>> where C : struct; sealed class Q : ComponentQuery<T>;")]
    [TestCase("sealed class Of<C> : Query<Span<C>> where C : struct; static class R { static void Read(World world) => world.Query<Of<Entity>>(); }")]
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

            sealed class S : ISystem<Game.Of<Speed>, HeatQuery>
            {
                public void Update(Game.Of<Speed> speeds, HeatQuery heats)
                {
                }
            }

            static class Reader
            {
                static void Read(World world) => world.Query<Game.Of<Light>>();
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

        Assert.That(diagnostics.Select(d => d.Id), Is.EqualTo(new[] { "PXECS017" }));
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
        string[] platformAssemblies = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
        IEnumerable<MetadataReference> references = platformAssemblies
            .Append(typeof(World).Assembly.Location)
            .Append(typeof(Pixely.DependencyInjection.ServiceCollection).Assembly.Location)
            .Append(typeof(IUpdatable).Assembly.Location)
            .Select(path => MetadataReference.CreateFromFile(path));

        return CSharpCompilation.Create(
            "TestAssembly",
            sources.Select((source, index) => CSharpSyntaxTree.ParseText(source, ParseOptions, $"Source{index}.cs")),
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary).WithNullableContextOptions(NullableContextOptions.Enable));
    }

    private static GeneratorDriver CreateDriver() =>
        CSharpGeneratorDriver.Create([new QueryTermGenerator().AsSourceGenerator(), new SystemGenerator().AsSourceGenerator(), new TagGenerator().AsSourceGenerator()],
            parseOptions: ParseOptions);
}
