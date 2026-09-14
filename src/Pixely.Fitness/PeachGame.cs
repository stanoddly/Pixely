using System.Reflection;
using System.Runtime.CompilerServices;
using Pixely.App;
using Pixely.DependencyInjection;

namespace Pixely.Fitness;

/// <summary>
/// What the document lets the rules derive from the prefix alone: the assemblies by their fixed names,
/// the state root as the one State class nothing else in State holds, the two containers composed by
/// invoking every registrar with default arguments, and the repository root by the Game project file.
/// What could not be derived is a violation of rule 00, and a rule that needs the missing piece skips.
/// </summary>
internal sealed class PeachGame
{
    private PeachGame(PeachArchitectureOptions options)
    {
        List<string> violations = new List<string>();
        Game = Load(options.GameNamespace, required: true);
        Frontend = Load(options.FrontendNamespace, required: true);
        Rendering = Load(options.RenderingNamespace, required: false);
        Audio = Load(options.AudioNamespace, required: false);
        Ai = Load(options.AiNamespace, required: false);
        Scenario = Load(options.ScenarioNamespace, required: false);
        Executable = Load(options.ExecutableNamespace, required: false) ?? Load(options.GamePrefix, required: false);
        if (Executable == null)
        {
            violations.Add($"assembly not found: {options.ExecutableNamespace} or {options.GamePrefix}");
        }

        RepositoryRoot = FindRepositoryRoot(options);
        if (Game != null && Frontend != null && Executable != null)
        {
            StateRoot = FindStateRoot(options, violations);
            RootRegistrations = Compose(options, typeof(PixelyAppBuilder), new PixelyAppBuilder(), violations);
            StageRegistrations = Compose(options, typeof(ServiceCollection), new ServiceCollection(), violations);
            if (RepositoryRoot != null)
            {
                violations.AddRange(ProjectInventory(options));
            }
        }

        Violations = violations;

        Assembly? Load(string name, bool required)
        {
            try
            {
                return Assembly.Load(new AssemblyName(name));
            }
            catch (FileNotFoundException)
            {
                if (required)
                {
                    violations.Add($"assembly not found: {name}");
                }

                return null;
            }
        }
    }

    internal Assembly? Game { get; }
    internal Assembly? Frontend { get; }
    internal Assembly? Rendering { get; }
    internal Assembly? Audio { get; }
    internal Assembly? Ai { get; }
    internal Assembly? Scenario { get; }
    internal Assembly? Executable { get; }
    internal Type? StateRoot { get; }
    internal string? RepositoryRoot { get; }
    internal IReadOnlyList<ServiceRegistration> RootRegistrations { get; } = [];
    internal IReadOnlyList<ServiceRegistration> StageRegistrations { get; } = [];
    internal IReadOnlyList<string> Violations { get; }

    internal bool IsComplete => Game != null && Frontend != null && Executable != null;

    internal static PeachGame Resolve(PeachArchitectureOptions options)
    {
        return new PeachGame(options);
    }

    // The public static classes in a project's root namespace are its registrars: Add* extension methods on PixelyAppBuilder for root, on ServiceCollection for a stage.
    internal static bool IsRegistrarMethod(MethodInfo method)
    {
        ParameterInfo[] parameters = method.GetParameters();
        return method.GetCustomAttribute<ExtensionAttribute>() != null
            && method.Name.StartsWith("Add", StringComparison.Ordinal)
            && parameters.Length > 0
            && (parameters[0].ParameterType == typeof(PixelyAppBuilder) || parameters[0].ParameterType == typeof(ServiceCollection));
    }

    internal IEnumerable<MethodInfo> Registrars(PeachArchitectureOptions options, Type container)
    {
        return new[] { Game, Frontend, Rendering, Audio, Ai, Scenario }.OfType<Assembly>()
            .SelectMany(assembly => TypeGraph.DeclaredTypes(assembly).Where(type => type.IsPublic && TypeGraph.IsStatic(type) && type.Namespace == RootNamespaceOf(options, assembly)))
            .SelectMany(TypeGraph.DeclaredMethods)
            .Where(method => method.IsPublic && !method.IsGenericMethodDefinition && IsRegistrarMethod(method) && method.GetParameters()[0].ParameterType == container)
            .OrderBy(method => method.DeclaringType!.FullName, StringComparer.Ordinal)
            .ThenBy(method => method.Name, StringComparer.Ordinal);
    }

    // A registrar registers the same types whatever its arguments, so defaults compose the real set of registrations.
    private IReadOnlyList<ServiceRegistration> Compose(PeachArchitectureOptions options, Type container, ServiceCollection target, List<string> violations)
    {
        return RegistrarProbe.Compose(Registrars(options, container), target, violations);
    }

    // The state root is the one State class no other State type holds. A field typed by a base or an interface holds every type assignable to it.
    private Type? FindStateRoot(PeachArchitectureOptions options, List<string> violations)
    {
        Type[] stateTypes = TypeGraph.TypesIn(Game!, options.StateNamespace).ToArray();
        HashSet<Type> held = stateTypes.SelectMany(TypeGraph.DeclaredFields).Select(field => field.FieldType).SelectMany(TypeGraph.Expand).ToHashSet();
        Type[] roots = stateTypes.Where(type => type.IsClass && !TypeGraph.IsStatic(type) && !type.IsNested && !held.Any(heldType => heldType.IsAssignableFrom(type))).ToArray();
        if (roots.Length == 1)
        {
            return roots[0];
        }

        violations.Add($"state roots in {options.StateNamespace}: {(roots.Length == 0 ? "none" : string.Join(", ", roots.Select(type => type.FullName)))}, expected one");
        return null;
    }

    // Projects sit at src/Foo.Part/Foo.Part.csproj. Every Foo.* directory there is a documented part whose assembly loaded, and vice versa.
    private IEnumerable<string> ProjectInventory(PeachArchitectureOptions options)
    {
        string source = Path.Combine(RepositoryRoot!, "src");
        HashSet<string> expected = new[] { Game, Frontend, Rendering, Audio, Ai, Scenario }.OfType<Assembly>().Select(assembly => assembly.GetName().Name!)
            .Append(options.ExecutableNamespace)
            .ToHashSet(StringComparer.Ordinal);
        string[] actual = Directory.EnumerateDirectories(source, $"{options.GamePrefix}.*").Select(directory => Path.GetFileName(directory)).Order(StringComparer.Ordinal).ToArray();
        return actual.Where(name => !expected.Contains(name)).Select(name => $"project not in the document or not loaded: src/{name}")
            .Concat(expected.Where(name => !actual.Contains(name)).Order(StringComparer.Ordinal).Select(name => $"project missing: src/{name}"));
    }

    // Matched against the loaded assemblies here, not through options.Resolved, because the constructor asks before Resolved is set.
    internal string RootNamespaceOf(PeachArchitectureOptions options, Assembly assembly)
    {
        if (assembly == Game)
        {
            return options.GameNamespace;
        }

        if (assembly == Frontend)
        {
            return options.FrontendNamespace;
        }

        if (assembly == Rendering)
        {
            return options.RenderingNamespace;
        }

        if (assembly == Audio)
        {
            return options.AudioNamespace;
        }

        if (assembly == Ai)
        {
            return options.AiNamespace;
        }

        if (assembly == Scenario)
        {
            return options.ScenarioNamespace;
        }

        return options.ExecutableNamespace;
    }

    private static string? FindRepositoryRoot(PeachArchitectureOptions options)
    {
        for (DirectoryInfo? directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "src", options.GameNamespace, $"{options.GameNamespace}.csproj")))
            {
                return directory.FullName;
            }
        }

        return null;
    }
}
