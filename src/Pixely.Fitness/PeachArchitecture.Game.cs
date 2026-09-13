using System.Reflection;
using Pixely;
using Pixely.Observations;

namespace Pixely.Fitness;

public static partial class PeachArchitecture
{
    private static readonly Type[] PrimitiveIdTypes = [typeof(int), typeof(uint), typeof(long), typeof(ulong), typeof(short), typeof(ushort), typeof(string), typeof(Guid)];
    private static readonly Type[] ReadOnlyCollectionDefinitions = [typeof(IReadOnlyList<>), typeof(IReadOnlyDictionary<,>), typeof(ReadOnlySpan<>)];

    // 10. Game never pushes: no events, no delegate fields, so a reader can only poll State or drain the log.
    private static IReadOnlyList<string> GameNeverPushes(PeachArchitectureOptions options)
    {
        List<string> violations = new List<string>();
        foreach (Type type in TypeGraph.DeclaredTypes(options.Game))
        {
            violations.AddRange(type.GetEvents(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .Select(member => $"event {TypeGraph.Describe(member)}"));
            violations.AddRange(TypeGraph.DeclaredFields(type).Where(field => typeof(Delegate).IsAssignableFrom(field.FieldType)).Select(field => $"delegate field {TypeGraph.Describe(field)}"));
        }

        return violations;
    }

    // 11. What Game shows is usable: no public member names an internal type.
    private static IReadOnlyList<string> PublicGameMembersExposeOnlyPublicTypes(PeachArchitectureOptions options)
    {
        return TypeGraph.DeclaredTypes(options.Game)
            .Where(TypeGraph.IsPublicSurface)
            .SelectMany(type => TypeGraph.PublicSignatureTypes(type).Where(named => !named.IsVisible && !named.IsGenericParameter).Select(named => $"{type.FullName}: {named.FullName}"))
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    // 11a. A public type in Mechanics that no public Mechanic exposes, directly or through an outcome's members, MUST be internal.
    private static IReadOnlyList<string> UnexposedMechanicsTypesAreInternal(PeachArchitectureOptions options)
    {
        Type[] publicMechanics = PublicMechanics(options).ToArray();
        HashSet<Type> exposed = new HashSet<Type>();
        Queue<Type> pending = new Queue<Type>(publicMechanics.SelectMany(TypeGraph.PublicSignatureTypes));
        while (pending.TryDequeue(out Type? type))
        {
            if (type.Namespace == options.MechanicsNamespace && exposed.Add(type))
            {
                foreach (Type member in TypeGraph.PublicSignatureTypes(type).Concat(type.GetProperties(BindingFlags.Instance | BindingFlags.Public).Select(property => property.PropertyType).SelectMany(TypeGraph.Expand)))
                {
                    pending.Enqueue(member);
                }
            }
        }

        return TypeGraph.TypesIn(options.Game, options.MechanicsNamespace)
            .Where(type => TypeGraph.IsPublicSurface(type) && !publicMechanics.Contains(type) && !exposed.Contains(type))
            .Select(type => type.FullName!)
            .ToArray();
    }

    // 12. Public types live in Vocabulary, State, Mechanics, Observations, the game's extras, and the root registrars.
    private static IReadOnlyList<string> GamePublicTypesLiveInTheirNamespaces(PeachArchitectureOptions options)
    {
        HashSet<string> allowed = new[] { options.VocabularyNamespace, options.StateNamespace, options.MechanicsNamespace, options.ObservationsNamespace }
            .Concat(options.ExtraGameNamespaces).ToHashSet(StringComparer.Ordinal);
        return TypeGraph.DeclaredTypes(options.Game)
            .Where(type => type.IsPublic && !(allowed.Contains(type.Namespace ?? string.Empty) || type.Namespace == options.GameNamespace && TypeGraph.IsStatic(type)))
            .Select(type => type.FullName!)
            .ToArray();
    }

    // 13. No command records, no dispatcher.
    private static IReadOnlyList<string> NoCommandsHandlersOrDispatchers(PeachArchitectureOptions options)
    {
        return TypeGraph.DeclaredTypes(options.Game)
            .Where(type => type.Name.EndsWith("Command", StringComparison.Ordinal) || type.Name.EndsWith("Handler", StringComparison.Ordinal) || type.Name.EndsWith("Dispatcher", StringComparison.Ordinal))
            .Select(type => type.FullName!)
            .ToArray();
    }

    // 14. Vocabulary is primitives: enums, value types, and static holders of them.
    private static IReadOnlyList<string> VocabularyHoldsOnlyPrimitives(PeachArchitectureOptions options)
    {
        return TypeGraph.TypesIn(options.Game, options.VocabularyNamespace)
            .Where(type => !type.IsEnum && !type.IsValueType && !TypeGraph.IsStatic(type))
            .Select(type => type.FullName!)
            .ToArray();
    }

    // 15. An id is its own type.
    private static IReadOnlyList<string> IdsAreTheirOwnTypes(PeachArchitectureOptions options)
    {
        List<string> violations = new List<string>();
        foreach (Type type in TypeGraph.DeclaredTypes(options.Game).Where(TypeGraph.IsPublicSurface))
        {
            violations.AddRange(type.GetProperties(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly)
                .Where(property => IsPrimitiveId(property.Name, property.PropertyType)).Select(TypeGraph.Describe));
            violations.AddRange(TypeGraph.DeclaredFields(type).Where(field => field.IsPublic && IsPrimitiveId(field.Name, field.FieldType)).Select(TypeGraph.Describe));
            violations.AddRange(TypeGraph.DeclaredMethods(type).Where(method => method.IsPublic).Concat<MethodBase>(TypeGraph.Constructors(type).Where(constructor => constructor.IsPublic))
                .SelectMany(method => method.GetParameters().Where(parameter => IsPrimitiveId(parameter.Name!, parameter.ParameterType)).Select(parameter => $"{TypeGraph.Describe(method)}({parameter.Name})")));
        }

        return violations;

        static bool IsPrimitiveId(string name, Type type)
        {
            return name.EndsWith("Id", StringComparison.Ordinal) && PrimitiveIdTypes.Contains(Nullable.GetUnderlyingType(type) ?? type);
        }
    }

    // 16. One state root per stage: the stage registers the root and no other State type.
    private static IReadOnlyList<string> StageRegistersExactlyTheStateRoot(PeachArchitectureOptions options)
    {
        Type[] registered = StageRegistrations(options)
            .SelectMany(registration => new[] { registration.ServiceType, registration.ConcreteType })
            .OfType<Type>()
            .Where(type => type.Namespace == options.StateNamespace)
            .Distinct()
            .ToArray();
        List<string> violations = registered.Where(type => type != options.StateRoot).Select(type => $"registered: {type.FullName}").ToList();
        if (!registered.Contains(options.StateRoot))
        {
            violations.Add($"missing: {options.StateRoot.FullName}");
        }

        return violations;
    }

    // 17. A public property MUST NOT have a setter. An init accessor is construction by another syntax, the shape of a payload record a
    // caller builds, so it is not a setter here.
    private static IReadOnlyList<string> StateHasNoPublicSetters(PeachArchitectureOptions options)
    {
        return PublicStateProperties(options)
            .Where(property => property.SetMethod?.IsPublic == true && !IsInitOnly(property))
            .Select(TypeGraph.Describe)
            .ToArray();
    }

    // 18. A public collection is one of the three read-only shapes.
    private static IReadOnlyList<string> StateCollectionsAreReadOnly(PeachArchitectureOptions options)
    {
        return PublicStateProperties(options)
            .Where(property => TypeGraph.IsEnumerable(property.PropertyType) && !IsReadOnlyCollection(property.PropertyType))
            .Select(TypeGraph.Describe)
            .ToArray();
    }

    // 19. State holds no logic, so nothing in it, attributes included, names Mechanics, Systems or Observations.
    private static IReadOnlyList<string> StateNamesNoRules(PeachArchitectureOptions options)
    {
        string[] ruleNamespaces = [options.MechanicsNamespace, options.SystemsNamespace, options.ObservationsNamespace];
        return TypeGraph.TypesIn(options.Game, options.StateNamespace)
            .SelectMany(type => TypeGraph.SignatureTypes(type).Where(named => ruleNamespaces.Contains(named.Namespace)).Select(named => $"{type.FullName} -> {named.FullName}"))
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    // 20. A read returns the live thing, never a transport record.
    private static IReadOnlyList<string> StateReadsReturnNoTransportRecords(PeachArchitectureOptions options)
    {
        return TypeGraph.TypesIn(options.Game, options.StateNamespace)
            .Where(TypeGraph.IsPublicSurface)
            .SelectMany(type => TypeGraph.DeclaredMethods(type).Where(method => method.IsPublic && method.ReturnType != typeof(void)))
            .Where(method => !IsStateReadType(options, method.ReturnType))
            .Select(TypeGraph.Describe)
            .ToArray();
    }

    // 21. Everything reachable from the state root is State or Vocabulary, or a primitive, so the rules above see all of it.
    private static IReadOnlyList<string> StateGraphStaysInState(PeachArchitectureOptions options)
    {
        List<string> violations = new List<string>();
        HashSet<Type> visited = new HashSet<Type>();
        Visit(options.StateRoot);
        return violations;

        void Visit(Type type)
        {
            Type? underlying = Nullable.GetUnderlyingType(type);
            if (underlying != null)
            {
                Visit(underlying);
                return;
            }

            if (type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal) || type == typeof(DateTime) || type == typeof(TimeSpan) || type == typeof(Guid))
            {
                return;
            }

            if (typeof(Delegate).IsAssignableFrom(type) || type.IsPointer || type.IsByRef)
            {
                violations.Add(type.FullName!);
                return;
            }

            if (type.IsArray)
            {
                Visit(type.GetElementType()!);
                return;
            }

            // A framework container is storage, not state; what it holds is.
            if (type.IsGenericType && type.Namespace?.StartsWith("System", StringComparison.Ordinal) == true)
            {
                foreach (Type argument in type.GetGenericArguments())
                {
                    Visit(argument);
                }

                return;
            }

            if (!visited.Add(type))
            {
                return;
            }

            if (type.Assembly != options.Game || type.Namespace != options.StateNamespace && type.Namespace != options.VocabularyNamespace)
            {
                violations.Add(type.FullName!);
                return;
            }

            for (Type? current = type; current != null && current != typeof(object); current = current.BaseType)
            {
                foreach (FieldInfo field in current.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    Visit(field.FieldType);
                }
            }

            if (type.IsAbstract || type.IsInterface)
            {
                foreach (Type implementation in TypeGraph.DeclaredTypes(options.Game).Where(candidate => candidate != type && !candidate.IsAbstract && type.IsAssignableFrom(candidate)))
                {
                    Visit(implementation);
                }
            }
        }
    }

    // 22. Only Game creates State: a class it writes internally has no public constructor. Payload records are the caller's to build.
    private static IReadOnlyList<string> StateIsCreatedOnlyByGame(PeachArchitectureOptions options)
    {
        return TypeGraph.TypesIn(options.Game, options.StateNamespace)
            .Where(type => TypeGraph.IsPublicSurface(type) && type.IsClass && !TypeGraph.IsStatic(type))
            .Where(type => HasInternalSetter(type) && TypeGraph.Constructors(type).Any(constructor => constructor.IsPublic))
            .Select(type => type.FullName!)
            .ToArray();
    }

    // 23. Public classes in Mechanics are Mechanics; every other public type there is an outcome shape, an enum or a readonly record struct.
    private static IReadOnlyList<string> MechanicsPublicSurfaceIsMechanicsAndOutcomes(PeachArchitectureOptions options)
    {
        return TypeGraph.TypesIn(options.Game, options.MechanicsNamespace)
            .Where(TypeGraph.IsPublicSurface)
            .Where(type => type.IsClass ? !type.Name.EndsWith("Mechanic", StringComparison.Ordinal) : !type.IsEnum && !TypeGraph.IsReadOnlyRecordStruct(type))
            .Select(type => type.FullName!)
            .ToArray();
    }

    // 24. The registrar is the only way in.
    private static IReadOnlyList<string> MechanicsHaveNoPublicConstructors(PeachArchitectureOptions options)
    {
        if (!options.MechanicsHaveNoPublicConstructors)
        {
            return [];
        }

        return PublicMechanics(options).Where(type => TypeGraph.Constructors(type).Any(constructor => constructor.IsPublic)).Select(type => type.FullName!).ToArray();
    }

    // 25. The state root arrives through the constructor, never as a parameter, and no static rule takes ambient state. Ambient is what the
    // root holds directly; an element handed to a helper is the unit of work, the same shape as Step(activity).
    private static IReadOnlyList<string> MechanicsTakeTheStateRootThroughConstructors(PeachArchitectureOptions options)
    {
        if (!options.MechanicsTakeStateRootThroughConstructor)
        {
            return [];
        }

        // Live state: what the root holds and Game writes internally. A payload record with public init is a value a caller builds.
        HashSet<Type> ambient = options.StateRoot.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Select(property => property.PropertyType)
            .Where(type => type.Namespace == options.StateNamespace && HasInternalSetter(type))
            .Append(options.StateRoot)
            .ToHashSet();
        Type[] mechanics = TypeGraph.TypesIn(options.Game, options.MechanicsNamespace).Where(type => type.IsClass && type.Name.EndsWith("Mechanic", StringComparison.Ordinal)).ToArray();
        List<string> violations = mechanics
            .Where(type => !TypeGraph.Constructors(type).Any(constructor => constructor.GetParameters().Any(parameter => parameter.ParameterType == options.StateRoot)))
            .Select(type => $"no root in constructor: {type.FullName}")
            .ToList();
        violations.AddRange(mechanics.SelectMany(TypeGraph.DeclaredMethods)
            .Where(method => method.GetParameters().Any(parameter => parameter.ParameterType == options.StateRoot))
            .Select(method => $"root as parameter: {TypeGraph.Describe(method)}"));
        violations.AddRange(TypeGraph.DeclaredTypes(options.Game)
            .Where(type => type.Namespace == options.MechanicsNamespace || type.Namespace == options.SystemsNamespace)
            .SelectMany(TypeGraph.DeclaredMethods)
            .Where(method => method.IsStatic && !method.IsPrivate && method.GetParameters().Any(parameter => ambient.Contains(parameter.ParameterType)))
            .Select(method => $"ambient state in static: {TypeGraph.Describe(method)}"));
        return violations;
    }

    // 26. An outcome is semantic, never a user facing message.
    private static IReadOnlyList<string> MechanicsReturnNoStrings(PeachArchitectureOptions options)
    {
        return PublicMechanics(options)
            .SelectMany(TypeGraph.DeclaredMethods)
            .Where(method => method.IsPublic && method.ReturnType == typeof(string))
            .Select(TypeGraph.Describe)
            .ToArray();
    }

    // 27. Output and Executable name no Mechanic, so they cannot invoke one.
    private static IReadOnlyList<string> OnlyDirectorsAndActorsNameMechanics(PeachArchitectureOptions options)
    {
        HashSet<Type> mechanics = PublicMechanics(options).ToHashSet();
        return options.OutputAssemblies.Append(options.Executable)
            .SelectMany(TypeGraph.DeclaredTypes)
            .SelectMany(type => TypeGraph.SignatureTypes(type).Where(mechanics.Contains).Select(mechanic => $"{type.FullName} -> {mechanic.FullName}"))
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    // 28 and 29. Systems are internal updatables.
    private static IReadOnlyList<string> SystemsAreInternalUpdatables(PeachArchitectureOptions options)
    {
        return TypeGraph.TypesIn(options.Game, options.SystemsNamespace)
            .Where(type => TypeGraph.IsPublicSurface(type) || type.IsClass && !type.IsAbstract && !TypeGraph.IsStatic(type) && !typeof(IUpdatable).IsAssignableFrom(type))
            .Select(type => type.FullName!)
            .ToArray();
    }

    // 32. An entry is a past tense record of ids and value types, named with the Entry suffix.
    private static IReadOnlyList<string> EntriesAreRecordsOfIdsAndValues(PeachArchitectureOptions options)
    {
        List<string> violations = new List<string>();
        foreach (Type type in TypeGraph.TypesIn(options.Game, options.ObservationsNamespace).Where(type => !type.IsEnum && !type.IsInterface && !TypeGraph.IsStatic(type)))
        {
            if (!type.Name.EndsWith("Entry", StringComparison.Ordinal) || !(type.IsValueType || TypeGraph.IsRecord(type)))
            {
                violations.Add($"not an entry: {type.FullName}");
            }

            violations.AddRange(TypeGraph.DeclaredFields(type).Where(field => !field.IsStatic)
                .Select(field => Nullable.GetUnderlyingType(field.FieldType) ?? field.FieldType)
                .Where(fieldType => !fieldType.IsValueType)
                .Select(fieldType => $"{type.FullName} carries {fieldType.FullName}"));
        }

        return violations;
    }

    // 33. One log per stage, and only when there are entries to carry.
    private static IReadOnlyList<string> StageRegistersOneLogWhenEntriesExist(PeachArchitectureOptions options)
    {
        Type[] logs = StageRegistrations(options)
            .Select(registration => registration.ServiceType)
            .Where(type => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ObservationLog<>))
            .ToArray();
        bool hasEntries = TypeGraph.TypesIn(options.Game, options.ObservationsNamespace).Any(type => type.Name.EndsWith("Entry", StringComparison.Ordinal));
        List<string> violations = new List<string>();
        if (logs.Length != (hasEntries ? 1 : 0))
        {
            violations.Add($"logs registered: {logs.Length}, entry types: {(hasEntries ? "present" : "none")}");
        }

        violations.AddRange(logs.Select(log => log.GetGenericArguments()[0]).Where(entry => entry.Namespace != options.ObservationsNamespace).Select(entry => $"entry outside Observations: {entry.FullName}"));
        return violations;
    }

    // 34. Game writes the log and never reads it; Frontend and the actors read, nobody else, and a holder keeps one reader.
    private static IReadOnlyList<string> OnlyDirectorsAndActorsHoldReaders(PeachArchitectureOptions options)
    {
        HashSet<Assembly> readers = options.ActorAssemblies.Append(options.Frontend).ToHashSet();
        IEnumerable<string> outsiders = options.ProductionAssemblies
            .Where(assembly => !readers.Contains(assembly))
            .SelectMany(TypeGraph.DeclaredTypes)
            .SelectMany(type => TypeGraph.DeclaredFields(type).Where(field => IsReader(field.FieldType)).Select(TypeGraph.Describe));
        IEnumerable<string> hoarders = readers
            .SelectMany(TypeGraph.DeclaredTypes)
            .Where(type => TypeGraph.DeclaredFields(type).Count(field => IsReader(field.FieldType)) > 1)
            .Select(type => $"more than one reader: {type.FullName}");
        return outsiders.Concat(hoarders).ToArray();

        static bool IsReader(Type type)
        {
            return type.IsGenericType && (type.GetGenericTypeDefinition() == typeof(ObservationReader<>) || type.GetGenericTypeDefinition() == typeof(ParticipantObservationReader<,>));
        }
    }

    private static IEnumerable<Type> PublicMechanics(PeachArchitectureOptions options)
    {
        return TypeGraph.TypesIn(options.Game, options.MechanicsNamespace).Where(type => type.IsPublic && type.IsClass && type.Name.EndsWith("Mechanic", StringComparison.Ordinal));
    }

    private static IEnumerable<PropertyInfo> PublicStateProperties(PeachArchitectureOptions options)
    {
        return TypeGraph.TypesIn(options.Game, options.StateNamespace)
            .Where(TypeGraph.IsPublicSurface)
            .SelectMany(type => type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly));
    }

    private static bool IsReadOnlyCollection(Type type)
    {
        return type.IsGenericType && ReadOnlyCollectionDefinitions.Contains(type.GetGenericTypeDefinition());
    }

    private static bool HasInternalSetter(Type type)
    {
        return type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly).Any(property => property.SetMethod is { IsPublic: false });
    }

    private static bool IsInitOnly(PropertyInfo property)
    {
        return property.SetMethod!.ReturnParameter.GetRequiredCustomModifiers().Any(modifier => modifier.FullName == "System.Runtime.CompilerServices.IsExternalInit");
    }

    private static bool IsStateReadType(PeachArchitectureOptions options, Type type)
    {
        Type inner = Nullable.GetUnderlyingType(type) ?? type;
        if (inner.IsPrimitive || inner.IsEnum || inner == typeof(string) || inner == typeof(decimal) || inner == typeof(DateTime) || inner == typeof(TimeSpan) || inner == typeof(Guid))
        {
            return true;
        }

        if (IsReadOnlyCollection(inner))
        {
            return inner.GetGenericArguments().All(argument => IsStateReadType(options, argument));
        }

        return inner.Assembly == options.Game && (inner.Namespace == options.StateNamespace || inner.Namespace == options.VocabularyNamespace);
    }
}
