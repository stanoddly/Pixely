using System.Reflection;
using System.Runtime.CompilerServices;
using Pixely;
using Pixely.DependencyInjection;
using Pixely.RenderOrchestration;
using Pixely.Ui;

namespace Pixely.Fitness;

/// <summary>
/// The fitness functions of the Peach architecture document, one per rule, each returning the members
/// that break it. Numbers refer to the test plan.
/// </summary>
public static partial class PeachArchitecture
{
    private static readonly string[] FrameworkAssemblyPrefixes = ["Pixely", "System", "Microsoft", "netstandard", "mscorlib"];

    public static FitnessReport Evaluate(PeachArchitectureOptions options)
    {
        FitnessResult resolution = new FitnessResult("00 GameResolvesFromPrefix", options.Resolved.Violations);
        if (!options.Resolved.IsComplete)
        {
            return new FitnessReport([resolution]);
        }

        return new FitnessReport(
        [
            resolution,
            Run("01 ProjectReferencesMatchTheGraph", static options => options.Resolved.RepositoryRoot == null ? [] : ProjectReferences.Violations(options.Resolved.RepositoryRoot, options)),
            Run("02 AssemblyReferencesMatchTheGraph", AssemblyReferencesMatchTheGraph),
            Run("03 NoInternalAccessBetweenProductionAssemblies", NoInternalAccessBetweenProductionAssemblies),
            Run("05 RegistrarsAreTheOnlyRegistration", RegistrarsAreTheOnlyRegistration),
            Run("06 ExecutableOnlyComposes", ExecutableOnlyComposes),
            Run("07 RootDoesNotReachTheStage", RootDoesNotReachTheStage),
            Run("08 TypesLiveInDocumentedNamespaces", TypesLiveInDocumentedNamespaces),
            Run("09 NothingReturnsATask", NothingReturnsATask),
            Run("10 GameNeverPushes", GameNeverPushes),
            Run("11 PublicGameMembersExposeOnlyPublicTypes", PublicGameMembersExposeOnlyPublicTypes),
            Run("11a UnexposedMechanicsTypesAreInternal", UnexposedMechanicsTypesAreInternal),
            Run("12 GamePublicTypesLiveInTheirNamespaces", GamePublicTypesLiveInTheirNamespaces),
            Run("13 NoCommandsHandlersOrDispatchers", NoCommandsHandlersOrDispatchers),
            Run("14 VocabularyHoldsOnlyPrimitives", VocabularyHoldsOnlyPrimitives),
            Run("15 IdsAreTheirOwnTypes", IdsAreTheirOwnTypes),
            Run("16 StageRegistersExactlyTheStateRoot", StageRegistersExactlyTheStateRoot),
            Run("17 StateHasNoPublicSetters", StateHasNoPublicSetters),
            Run("18 StateCollectionsAreReadOnly", StateCollectionsAreReadOnly),
            Run("19 StateNamesNoRules", StateNamesNoRules),
            Run("20 StateReadsReturnNoTransportRecords", StateReadsReturnNoTransportRecords),
            Run("21 StateGraphStaysInState", StateGraphStaysInState),
            Run("22 StateIsCreatedOnlyByGame", StateIsCreatedOnlyByGame),
            Run("23 MechanicsPublicSurfaceIsMechanicsAndOutcomes", MechanicsPublicSurfaceIsMechanicsAndOutcomes),
            Run("24 MechanicsHaveNoPublicConstructors", MechanicsHaveNoPublicConstructors),
            Run("25 MechanicsTakeTheStateRootThroughConstructors", MechanicsTakeTheStateRootThroughConstructors),
            Run("26 MechanicsReturnNoStrings", MechanicsReturnNoStrings),
            Run("27 OnlyDirectorsAndActorsNameMechanics", OnlyDirectorsAndActorsNameMechanics),
            Run("29 SystemsAreInternalUpdatables", SystemsAreInternalUpdatables),
            Run("32 EntriesAreRecordsOfIdsAndValues", EntriesAreRecordsOfIdsAndValues),
            Run("33 StageRegistersOneLogWhenEntriesExist", StageRegistersOneLogWhenEntriesExist),
            Run("34 OnlyDirectorsAndActorsHoldReaders", OnlyDirectorsAndActorsHoldReaders),
            Run("35 FormsOwnEveryUiView", FormsOwnEveryUiView),
            Run("36 FormsNameNoStateOrMechanics", FormsNameNoStateOrMechanics),
            Run("38 DirectorAndActorPublicSurfaceIsTheRegistrar", DirectorAndActorPublicSurfaceIsTheRegistrar),
            Run("39 OutputReadsNoInput", OutputReadsNoInput),
            Run("40 OutputOwnsNoViewModels", OutputOwnsNoViewModels),
            Run("41 OutputPublicSurfaceIsRegistrarItemsAndCamera", OutputPublicSurfaceIsRegistrarItemsAndCamera),
            Run("41a AudioRecordsNameOnlyVocabulary", AudioRecordsNameOnlyVocabulary),
            Run("43 ActorsOwnNoState", ActorsOwnNoState)
        ]);

        FitnessResult Run(string name, Func<PeachArchitectureOptions, IReadOnlyList<string>> function)
        {
            return new FitnessResult(name, function(options));
        }
    }

    // 2. The arrow graph, read from the compiled assemblies rather than the project files. Executable composes, so it may pull in any
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

    // 3. Mutation is internal, so an assembly that sees another's internals could write State.
    private static IReadOnlyList<string> NoInternalAccessBetweenProductionAssemblies(PeachArchitectureOptions options)
    {
        HashSet<string> productionNames = options.ProductionAssemblies.Select(assembly => assembly.GetName().Name!).ToHashSet(StringComparer.Ordinal);
        return options.ProductionAssemblies
            .SelectMany(assembly => assembly.GetCustomAttributes<InternalsVisibleToAttribute>().Select(attribute => (Owner: assembly, Friend: attribute.AssemblyName)))
            .Where(grant => productionNames.Contains(grant.Friend) || options.GameGrantsNoInternalAccess && grant.Owner == options.Game)
            .Select(grant => $"{grant.Owner.GetName().Name} -> {grant.Friend}")
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    // 5. The public static classes in a project's root namespace are its registrars: Add* extension methods on PixelyAppBuilder for root,
    // on ServiceCollection for a stage. Nothing else registers.
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

    // 6. Executable composes; it takes no part in the frame or the user interface itself.
    private static IReadOnlyList<string> ExecutableOnlyComposes(PeachArchitectureOptions options)
    {
        return TypeGraph.DeclaredTypes(options.Executable)
            .Where(type => typeof(IUpdatable).IsAssignableFrom(type) || typeof(IUiView).IsAssignableFrom(type) || typeof(IUiViewModel).IsAssignableFrom(type)
                || type.GetInterfaces().Any(implemented => implemented.IsGenericType && implemented.GetGenericTypeDefinition() == typeof(IRenderer<>)))
            .Select(type => type.FullName!)
            .ToArray();
    }

    // 7. Root MUST NOT hold a reference into a stage: nothing root registers keeps or is handed anything the stage registers. A root service
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

    // 8. Every type sits in a namespace the document names, or one the game declared on top with a justification. A namespace is reported
    // once, not once per type.
    private static IReadOnlyList<string> TypesLiveInDocumentedNamespaces(PeachArchitectureOptions options)
    {
        Dictionary<Assembly, HashSet<string>> allowed = new Dictionary<Assembly, HashSet<string>>
        {
            [options.Game] = new[] { options.GameNamespace, options.VocabularyNamespace, options.StateNamespace, options.MechanicsNamespace, options.SystemsNamespace, options.ObservationsNamespace }
                .Concat(options.ExtraGameNamespaceNames).ToHashSet(StringComparer.Ordinal),
            [options.Frontend] = [options.FrontendNamespace, options.FormsNamespace],
            [options.Executable] = [options.ExecutableNamespace]
        };
        foreach (Assembly assembly in options.OutputAssemblies.Concat(options.ActorAssemblies))
        {
            allowed[assembly] = [options.RootNamespaceOf(assembly)];
        }

        List<string> violations = allowed
            .SelectMany(pair => TypeGraph.DeclaredTypes(pair.Key).Where(type => !type.IsNested && !pair.Value.Contains(type.Namespace ?? string.Empty)))
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

    // 9. The frame is single threaded, so nothing hands work to another thread.
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
