using System.Reflection;
using System.Runtime.CompilerServices;
using Pixely;
using Pixely.DependencyInjection;
using Pixely.RenderOrchestration;
using Pixely.Ui;

namespace Pixely.Fitness;

/// <summary>
/// The fitness functions of the Peach architecture document, one per rule, each returning the members
/// that break it. Each rule points at one sentence of the document.
/// </summary>
public static partial class PeachArchitecture
{
    private static readonly string[] FrameworkAssemblyPrefixes = ["Pixely", "System", "Microsoft", "netstandard", "mscorlib"];

    public static FitnessReport Evaluate(PeachArchitectureOptions options)
    {
        FitnessResult resolution = new FitnessResult("GameResolvesFromPrefix", options.Resolved.Violations);
        if (!options.Resolved.IsComplete)
        {
            return new FitnessReport([resolution]);
        }

        return new FitnessReport(
        [
            resolution,
            Run("ProjectReferencesMatchTheGraph", static options => options.Resolved.RepositoryRoot == null ? [] : ProjectReferences.Violations(options.Resolved.RepositoryRoot, options)),
            Run("AssemblyReferencesMatchTheGraph", AssemblyReferencesMatchTheGraph),
            Run("NoInternalAccessBetweenProductionAssemblies", NoInternalAccessBetweenProductionAssemblies),
            Run("RegistrarsAreTheOnlyRegistration", RegistrarsAreTheOnlyRegistration),
            Run("ExecutableOnlyComposes", ExecutableOnlyComposes),
            Run("RootDoesNotReachTheStage", RootDoesNotReachTheStage),
            Run("TypesLiveInDocumentedNamespaces", TypesLiveInDocumentedNamespaces),
            Run("NothingReturnsATask", NothingReturnsATask),
            Run("GameNeverPushes", GameNeverPushes),
            Run("UnexposedMechanicsTypesAreInternal", UnexposedMechanicsTypesAreInternal),
            Run("NoCommandsHandlersOrDispatchers", NoCommandsHandlersOrDispatchers),
            Run("VocabularyHoldsOnlyPrimitives", VocabularyHoldsOnlyPrimitives),
            Run("IdsAreTheirOwnTypes", IdsAreTheirOwnTypes),
            Run("StageRegistersExactlyTheStateRoot", StageRegistersExactlyTheStateRoot),
            Run("StateHasNoPublicSetters", StateHasNoPublicSetters),
            Run("StateCollectionsAreReadOnly", StateCollectionsAreReadOnly),
            Run("StateNamesNoRules", StateNamesNoRules),
            Run("StateReadsReturnNoTransportRecords", StateReadsReturnNoTransportRecords),
            Run("StateGraphStaysInState", StateGraphStaysInState),
            Run("MechanicsPublicSurfaceIsMechanicsAndOutcomes", MechanicsPublicSurfaceIsMechanicsAndOutcomes),
            Run("MechanicsTakeTheStateRootThroughConstructors", MechanicsTakeTheStateRootThroughConstructors),
            Run("MechanicsReturnNoStrings", MechanicsReturnNoStrings),
            Run("OnlyFrontendAndActorsNameMechanics", OnlyFrontendAndActorsNameMechanics),
            Run("SystemsAreInternalUpdatables", SystemsAreInternalUpdatables),
            Run("EntriesAreRecordsOfIdsAndValues", EntriesAreRecordsOfIdsAndValues),
            Run("StageRegistersOneLogWhenEntriesExist", StageRegistersOneLogWhenEntriesExist),
            Run("OnlyFrontendAndActorsHoldReaders", OnlyFrontendAndActorsHoldReaders),
            Run("FormsOwnEveryUiView", FormsOwnEveryUiView),
            Run("FormsNameNoStateOrMechanics", FormsNameNoStateOrMechanics),
            Run("FrontendAndActorPublicSurfaceIsTheRegistrar", FrontendAndActorPublicSurfaceIsTheRegistrar),
            Run("OutputReadsNoInput", OutputReadsNoInput),
            Run("OutputOwnsNoViewModels", OutputOwnsNoViewModels),
            Run("ActorsOwnNoState", ActorsOwnNoState)
        ]);

        FitnessResult Run(string name, Func<PeachArchitectureOptions, IReadOnlyList<string>> function)
        {
            return new FitnessResult(name, function(options));
        }
    }

    // The arrow graph, read from the compiled assemblies rather than the project files. Executable composes, so it may pull in any
    // package; of the game's own assemblies it may reference only the production ones.
    private static IReadOnlyList<string> AssemblyReferencesMatchTheGraph(PeachArchitectureOptions options)
    {
        List<string> violations = new List<string>();
        HashSet<string> productionNames = options.ProductionAssemblies.Select(assembly => assembly.GetName().Name!).ToHashSet(StringComparer.Ordinal);
        violations.AddRange(options.Executable.GetReferencedAssemblies()
            .Select(reference => reference.Name!)
            .Where(name => name.StartsWith(options.GamePrefix + ".", StringComparison.Ordinal) && !productionNames.Contains(name))
            .Select(name => $"{options.Executable.GetName().Name} -> {name}"));
        Check(options.Game);
        foreach (Assembly output in options.OutputAssemblies.Concat(options.ActorAssemblies))
        {
            Check(output, options.Game);
        }

        Check(options.Frontend, new[] { options.Game, options.Rendering, options.Audio }.Where(assembly => assembly != null).ToArray()!);
        return violations;

        void Check(Assembly assembly, params Assembly[] allowed)
        {
            HashSet<string> allowedNames = allowed.Select(reference => reference.GetName().Name!).ToHashSet(StringComparer.Ordinal);
            violations.AddRange(assembly.GetReferencedAssemblies()
                .Select(reference => reference.Name!)
                .Where(name => !allowedNames.Contains(name) && !FrameworkAssemblyPrefixes.Any(prefix => name == prefix || name.StartsWith(prefix + ".", StringComparison.Ordinal)))
                .Select(name => $"{assembly.GetName().Name} -> {name}"));
        }
    }

    // Mutation is internal, so a production assembly that sees another's internals could write State.
    private static IReadOnlyList<string> NoInternalAccessBetweenProductionAssemblies(PeachArchitectureOptions options)
    {
        HashSet<string> productionNames = options.ProductionAssemblies.Select(assembly => assembly.GetName().Name!).ToHashSet(StringComparer.Ordinal);
        return options.ProductionAssemblies
            .SelectMany(assembly => assembly.GetCustomAttributes<InternalsVisibleToAttribute>().Select(attribute => (Owner: assembly, Friend: attribute.AssemblyName)))
            .Where(grant => productionNames.Contains(grant.Friend))
            .Select(grant => $"{grant.Owner.GetName().Name} -> {grant.Friend}")
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    // A registrar is a public static class in the project's root namespace, an Add* extension method on PixelyAppBuilder for root, on
    // ServiceCollection for a stage. That namespace holds nothing else public, and nothing else registers.
    private static IReadOnlyList<string> RegistrarsAreTheOnlyRegistration(PeachArchitectureOptions options)
    {
        List<string> violations = new List<string>();
        foreach (Assembly assembly in options.ProductionAssemblies.Where(assembly => assembly != options.Executable))
        {
            string rootNamespace = options.RootNamespaceOf(assembly);
            foreach (Type type in TypeGraph.DeclaredTypes(assembly).Where(type => type.IsPublic && TypeGraph.IsStatic(type) && type.Namespace == rootNamespace))
            {
                violations.AddRange(TypeGraph.DeclaredMethods(type).Where(method => method.IsPublic && !PeachGame.IsRegistrarMethod(method)).Select(TypeGraph.Describe));
            }

            violations.AddRange(TypeGraph.DeclaredTypes(assembly)
                .Where(type => type.Namespace != rootNamespace)
                .SelectMany(TypeGraph.DeclaredMethods)
                .Where(method => method.IsPublic && method.GetParameters().Length > 0 && typeof(ServiceCollection).IsAssignableFrom(method.GetParameters()[0].ParameterType))
                .Select(TypeGraph.Describe));
        }

        return violations;
    }

    // Executable composes; it takes no part in the frame or the user interface itself.
    private static IReadOnlyList<string> ExecutableOnlyComposes(PeachArchitectureOptions options)
    {
        return TypeGraph.DeclaredTypes(options.Executable)
            .Where(type => typeof(IUpdatable).IsAssignableFrom(type) || typeof(IUiView).IsAssignableFrom(type) || typeof(IUiViewModel).IsAssignableFrom(type)
                || type.GetInterfaces().Any(implemented => implemented.IsGenericType && implemented.GetGenericTypeDefinition() == typeof(IRenderer<>)))
            .Select(type => type.FullName!)
            .ToArray();
    }

    // Root MUST NOT hold a reference into a stage: nothing root registers keeps or is handed anything the stage registers. A root service
    // that creates a stage type, e.g. persistence loading the state root, hands it over and keeps nothing.
    private static IReadOnlyList<string> RootDoesNotReachTheStage(PeachArchitectureOptions options)
    {
        HashSet<Type> stageTypes = StageRegistrations(options)
            .SelectMany(registration => new[] { registration.ServiceType, registration.ConcreteType })
            .OfType<Type>()
            .Where(type => !IsFrameworkAssembly(type.Assembly))
            .ToHashSet();
        return RootRegistrations(options)
            .Select(registration => registration.ConcreteType)
            .OfType<Type>()
            .Where(type => !IsFrameworkAssembly(type.Assembly))
            .Distinct()
            .SelectMany(type => HeldTypes(type).Where(stageTypes.Contains).Select(stageType => $"{type.FullName} -> {stageType.FullName}"))
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToArray();

        static IEnumerable<Type> HeldTypes(Type type)
        {
            return TypeGraph.DeclaredFields(type).Select(field => field.FieldType)
                .Concat(TypeGraph.Constructors(type).SelectMany(constructor => constructor.GetParameters().Select(parameter => parameter.ParameterType)))
                .SelectMany(TypeGraph.Expand);
        }
    }

    // Every type in Game lives in a namespace the document names, or one the game added with a reason, so the namespace scoped rules see it.
    // The other projects' rules are project scoped, so their namespaces are theirs. A namespace is reported once, not once per type.
    private static IReadOnlyList<string> TypesLiveInDocumentedNamespaces(PeachArchitectureOptions options)
    {
        HashSet<string> allowed = new[] { options.GameNamespace, options.VocabularyNamespace, options.StateNamespace, options.MechanicsNamespace, options.SystemsNamespace, options.ObservationsNamespace }
            .Concat(options.ExtraGameNamespaceNames).ToHashSet(StringComparer.Ordinal);
        List<string> violations = TypeGraph.DeclaredTypes(options.Game)
            .Where(type => !type.IsNested && !allowed.Contains(type.Namespace ?? string.Empty))
            .Select(type => type.Namespace ?? "(global)")
            .Distinct()
            .Order(StringComparer.Ordinal)
            .Select(ns => $"{ns}: not in the document; move its types or list the namespace in ExtraGameNamespaces with a strong justification")
            .ToList();
        HashSet<string> populated = TypeGraph.DeclaredTypes(options.Game).Select(type => type.Namespace ?? string.Empty).ToHashSet(StringComparer.Ordinal);
        violations.AddRange(options.ExtraGameNamespaces.Where(extra => string.IsNullOrWhiteSpace(extra.Justification)).Select(extra => $"{extra.Namespace}: listed in ExtraGameNamespaces without a justification"));
        violations.AddRange(options.ExtraGameNamespaces.Where(extra => !populated.Contains(extra.Namespace)).Select(extra => $"{extra.Namespace}: listed in ExtraGameNamespaces but holds no types"));
        return violations;
    }

    // The frame is single threaded, so nothing hands work to another thread.
    private static IReadOnlyList<string> NothingReturnsATask(PeachArchitectureOptions options)
    {
        return options.ProductionAssemblies
            .SelectMany(TypeGraph.DeclaredTypes)
            .SelectMany(TypeGraph.DeclaredMethods)
            .Where(method => TypeGraph.Expand(method.ReturnType).Any(type => type == typeof(Task) || type == typeof(ValueTask)
                || type.IsGenericType && (type.GetGenericTypeDefinition() == typeof(Task<>) || type.GetGenericTypeDefinition() == typeof(ValueTask<>))))
            .Select(TypeGraph.Describe)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static bool IsFrameworkAssembly(Assembly assembly)
    {
        string name = assembly.GetName().Name!;
        return FrameworkAssemblyPrefixes.Any(prefix => name == prefix || name.StartsWith(prefix + ".", StringComparison.Ordinal));
    }

    private static IReadOnlyList<ServiceRegistration> RootRegistrations(PeachArchitectureOptions options)
    {
        return options.Resolved.RootRegistrations;
    }

    private static IReadOnlyList<ServiceRegistration> StageRegistrations(PeachArchitectureOptions options)
    {
        return options.Resolved.StageRegistrations;
    }
}
