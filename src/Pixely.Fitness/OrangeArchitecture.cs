using System.Reflection;
using Pixely.Ecs;
using Pixely.Messages;
using Pixely.RenderOrchestration;
using Pixely.Ui;

namespace Pixely.Fitness;

/// <summary>
/// The fitness functions of the Orange architecture, see docs/orange-architecture.md. They check types and constructors only.
/// </summary>
public static class OrangeArchitecture
{
    private static readonly Assembly EcsAssembly = typeof(World).Assembly;

    private static readonly Assembly MessagesAssembly = typeof(MessageWriter<>).Assembly;

    // Code outside systems that runs every frame. A view is checked by ViewsReadOnlyTheirViewModel.
    private static readonly Type[] OutsideSystemTypes = [typeof(IUpdatable), typeof(IRenderer<>)];

    public static FitnessReport Evaluate(OrangeArchitectureOptions options)
    {
        return new FitnessReport(
        [
            new FitnessResult("Orange ViewsReadOnlyTheirViewModel", ViewsReadOnlyTheirViewModel(options)),
            new FitnessResult("Orange RenderersReadNoMessages", RenderersReadNoMessages(options)),
            new FitnessResult("Orange OutsideSystemsTakeNoWritingQueries", OutsideSystemsTakeNoWritingQueries(options)),
            new FitnessResult("Orange OutsideSystemsTakeNoWritableUniques", OutsideSystemsTakeNoWritableUniques(options)),
            new FitnessResult("Orange ViewModelsHaveOneWriter", ViewModelsHaveOneWriter(options)),
            new FitnessResult("Orange VocabularyHoldsOnlyPrimitives", VocabularyHoldsOnlyPrimitives(options)),
            new FitnessResult("Orange ComponentsHoldNoReferences", ComponentsHoldNoReferences(options)),
            new FitnessResult("Orange TypesAreInNamedNamespaces", TypesAreInNamedNamespaces(options)),
            new FitnessResult("Orange SharedComponentsHaveADeclaredOrder", SharedComponentsHaveADeclaredOrder(options))
        ]);
    }

    // A namespace is reported once, not once per type. An added namespace is allowed as it is, without sub-namespaces, since only the document's
    // namespaces have them. A game that lacks a named namespace passes, because a game without messages or audio is a game too. An auxiliary
    // library's namespace is not the game's, so its assembly is not passed.
    public static IReadOnlyList<string> TypesAreInNamedNamespaces(OrangeArchitectureOptions options)
    {
        HashSet<string> extra = options.ExtraNamespaces.Select(added => added.Namespace).ToHashSet(StringComparer.Ordinal);
        HashSet<string> populated = options.Assemblies.SelectMany(TypeGraph.DeclaredTypes).Select(type => type.Namespace ?? string.Empty).ToHashSet(StringComparer.Ordinal);
        List<string> violations = populated.Where(ns => !options.IsNamed(ns) && !extra.Contains(ns)).Order(StringComparer.Ordinal)
            .Select(ns => $"{(ns.Length == 0 ? "(global)" : ns)}: not a namespace orange-architecture.md names; move its types, or list it in ExtraNamespaces with the reason the named ones did not do")
            .ToList();
        violations.AddRange(options.ExtraNamespaces.Where(added => string.IsNullOrWhiteSpace(added.Justification)).Select(added => $"{added.Namespace}: listed in ExtraNamespaces without a reason"));
        violations.AddRange(options.ExtraNamespaces.Where(added => !populated.Contains(added.Namespace)).Select(added => $"{added.Namespace}: listed in ExtraNamespaces but holds no types"));
        return violations;
    }

    // Two systems that name the same component or unique, one of them writing it, run in the order of the AddSystem calls unless IAfter or IBefore
    // orders them, directly or through other systems, so a reordered AddGame would change what the game does without a sign. Two accesses that
    // cannot reach the same entity, e.g. one with IWith<Player> and one with IWithout<Player>, do not count. A declared order costs no parallelism:
    // a write through a span or a unique applies at once, so such a pair could never run in parallel, and a reader that runs before a structural
    // writer still could. A message read before its writer arrives one update late, so it does not count. EntityDestroyer does not count either,
    // although a reader before it still sees the entities it destroys: it changes every component, so counting it would pair it with every system
    // that has a query or a lookup.
    // The rule cannot see which systems a stage adds. It reports two systems of different stages that share a component, which an order the
    // scheduler ignores then settles, and it counts an order through a system the stage does not add, which the scheduler drops. A generic system
    // is skipped, because only the AddSystem<T> call closes its type arguments.
    public static IReadOnlyList<string> SharedComponentsHaveADeclaredOrder(OrangeArchitectureOptions options)
    {
        Type[] systems = Classes(options).Where(type => typeof(ISystem).IsAssignableFrom(type) && !type.IsGenericTypeDefinition).OrderBy(type => type.FullName, StringComparer.Ordinal).ToArray();
        Dictionary<Type, Access[]> accesses = systems.ToDictionary(system => system, system => TypeGraph.Constructors(system)
            .SelectMany(constructor => constructor.GetParameters()).SelectMany(parameter => TypeGraph.Expand(parameter.ParameterType)).SelectMany(Accesses).ToArray());
        Dictionary<Type, HashSet<Type>> later = Later(systems);
        List<string> violations = new List<string>();
        for (int i = 0; i < systems.Length; i++)
        {
            for (int j = i + 1; j < systems.Length; j++)
            {
                Type first = systems[i];
                Type second = systems[j];
                string[] shared = accesses[first].SelectMany(one => accesses[second].Where(other => Meet(one, other)).Select(other => Name(one.Kind is AccessKind.Write or AccessKind.StructuralWrite ? one.State : other.State)))
                    .Distinct().Order(StringComparer.Ordinal).ToArray();
                if (shared.Length > 0 && !later[first].Contains(second) && !later[second].Contains(first))
                {
                    violations.Add($"{first.FullName} and {second.FullName}: share {string.Join(", ", shared)}, which at least one of them writes, and neither is ordered after the other with IAfter or IBefore, so the order of AddSystem calls decides");
                }
            }
        }

        return violations;
    }

    // A filter reads only whether an entity has a component. A structural write adds or removes one, or spawns an entity with it.
    private enum AccessKind { Read, Filter, Write, StructuralWrite }

    // A component or a unique a constructor parameter names, how it uses it, and the components an entity it reaches must have or lack. A unique
    // reaches no entity, and neither does a lookup, an adder or a remover, which reach any. Spawned holds every component a spawned entity has.
    private readonly record struct Access(Type State, bool IsUnique, AccessKind Kind, Type[] Required, Type[] Excluded, Type[]? Spawned);

    private static IEnumerable<Access> Accesses(Type type)
    {
        if (type.IsClass && type != typeof(Query) && BaseTypes(type).Contains(typeof(Query)))
        {
            Type[] terms = QueryArguments(type);
            Type[] with = FilterArguments(type, typeof(IWith));
            Type[] without = FilterArguments(type, typeof(IWithout));
            Type[] required = terms.Where(term => !IsOptional(term)).Select(Unwrap).Where(component => component != typeof(Entity)).Concat(with).ToArray();
            return terms.Select(term => (Component: Unwrap(term), Kind: IsWritingTerm(term) ? AccessKind.Write : AccessKind.Read)).Where(term => term.Component != typeof(Entity))
                .Concat(with.Concat(without).Select(filter => (Component: filter, Kind: AccessKind.Filter)))
                .Select(term => new Access(term.Component, false, term.Kind, required, without, null));
        }

        // A spawned entity has the spawner's components and the tags of its IWith<...>.
        if (BaseTypes(type).FirstOrDefault(candidate => candidate.IsGenericType && candidate.BaseType == typeof(Spawner)) is { } spawner)
        {
            Type[] spawned = spawner.GetGenericArguments().Concat(FilterArguments(type, typeof(IWith))).ToArray();
            return spawned.Select(component => new Access(component, false, AccessKind.StructuralWrite, spawned, [], spawned));
        }

        if (!type.IsGenericType || type.Assembly != EcsAssembly)
        {
            return [];
        }

        Type definition = type.GetGenericTypeDefinition();
        Type[] arguments = type.GetGenericArguments();
        AccessKind? kind = definition == typeof(ComponentAdder<>) || definition == typeof(ComponentRemover<>) ? AccessKind.StructuralWrite
            : definition == typeof(Unique<>) || definition.Name.StartsWith("ComponentLookup`", StringComparison.Ordinal) ? AccessKind.Write
            : definition == typeof(ReadOnlyUnique<>) || definition.Name.StartsWith("ReadOnlyComponentLookup`", StringComparison.Ordinal) ? AccessKind.Read
            : null;
        if (kind is not { } known)
        {
            return [];
        }

        bool isUnique = definition == typeof(Unique<>) || definition == typeof(ReadOnlyUnique<>);
        return arguments.Select(argument => new Access(argument, isUnique, known, [], [], null));
    }

    // A unique is written through its class and read through its read-only type, which the class implements. A write of a component's values
    // does not change which entities a filter on it reaches. A spawned entity has only its spawner's components, so a query that requires
    // another one cannot reach it when it is spawned, and a system that adds that component meets the query on its own.
    private static bool Meet(Access one, Access other)
    {
        bool same = one.IsUnique ? one.IsUnique == other.IsUnique && (one.State.IsAssignableFrom(other.State) || other.State.IsAssignableFrom(one.State)) : !other.IsUnique && one.State == other.State;
        bool disjoint = one.Required.Intersect(other.Excluded).Any() || other.Required.Intersect(one.Excluded).Any()
            || (one.Spawned is { } spawned && other.Required.Except(spawned).Any()) || (other.Spawned is { } otherSpawned && one.Required.Except(otherSpawned).Any());
        return same && (Affects(one, other) || Affects(other, one)) && !disjoint;

        static bool Affects(Access writer, Access reader) => writer.Kind == AccessKind.StructuralWrite || writer.Kind == AccessKind.Write && reader.Kind != AccessKind.Filter;
    }

    // The systems that run after each system, following IAfter<...> and IBefore<...> through any number of systems. The scheduler orders a system
    // against every added system that is an instance of the type argument, so a base class orders each system derived from it.
    private static Dictionary<Type, HashSet<Type>> Later(Type[] systems)
    {
        Dictionary<Type, Type[]> next = systems.ToDictionary(system => system, system => systems.Where(other => other != system
            && (FilterArguments(system, typeof(IBefore)).Any(target => target.IsAssignableFrom(other)) || FilterArguments(other, typeof(IAfter)).Any(target => target.IsAssignableFrom(system)))).ToArray());
        return systems.ToDictionary(system => system, system =>
        {
            HashSet<Type> reached = new HashSet<Type>();
            Stack<Type> pending = new Stack<Type>([system]);
            while (pending.TryPop(out Type? current))
            {
                foreach (Type successor in next[current])
                {
                    if (reached.Add(successor))
                    {
                        pending.Push(successor);
                    }
                }
            }

            return reached;
        });
    }

    // The type arguments of the IWith<...>, IWithout<...>, IAfter<...> or IBefore<...> interfaces a type implements, by their non-generic base.
    private static Type[] FilterArguments(Type type, Type kind)
    {
        return type.GetInterfaces().Where(candidate => candidate.IsGenericType && kind.IsAssignableFrom(candidate)).SelectMany(candidate => candidate.GetGenericArguments()).Distinct().ToArray();
    }

    private static bool IsOptional(Type term)
    {
        return term.IsGenericType && term.GetGenericTypeDefinition() == typeof(Optional<>);
    }

    private static Type Unwrap(Type type)
    {
        bool wrapped = type.IsGenericType && (type.GetGenericTypeDefinition() == typeof(Span<>) || type.GetGenericTypeDefinition() == typeof(ReadOnlySpan<>) || type.GetGenericTypeDefinition() == typeof(Optional<>));
        return wrapped ? Unwrap(type.GetGenericArguments()[0]) : type;
    }

    // A view shows its view model and passes intents to it, so the system that writes the view model is the one that reads the world and messages.
    public static IReadOnlyList<string> ViewsReadOnlyTheirViewModel(OrangeArchitectureOptions options)
    {
        return ConstructorViolations(Classes(options).Where(type => typeof(UiView).IsAssignableFrom(type)), static type =>
            typeof(ISystem).IsAssignableFrom(type) ? "a view calls its view model, not a system"
            : IsWorldAccess(type) ? "a view reads the world only through its view model"
            : IsMessageAccess(type) ? "a view reads and writes no message, the system that writes its view model does"
            : null);
    }

    // Pixely skips rendering while a window is minimized, so a renderer reading messages would miss them. Writing one is allowed like anywhere on
    // the main thread.
    public static IReadOnlyList<string> RenderersReadNoMessages(OrangeArchitectureOptions options)
    {
        return ConstructorViolations(Classes(options).Where(type => TypeGraph.Implements(type, typeof(IRenderer<>))), static type =>
            IsMessageAccess(type) && !(type.IsGenericType && type.GetGenericTypeDefinition() == typeof(MessageWriter<>))
                ? "a renderer reads no message, because Pixely skips rendering while a window is minimized; a separate updatable reads it and keeps what the renderer draws"
                : null);
    }

    // AddQuery already throws for a writing query, but only when the provider is built. A helper is a static function a system passes its query to,
    // so it has no constructor to check.
    public static IReadOnlyList<string> OutsideSystemsTakeNoWritingQueries(OrangeArchitectureOptions options)
    {
        IEnumerable<Type> outsideSystems = Classes(options).Where(type => !typeof(ISystem).IsAssignableFrom(type) && OutsideSystemTypes.Any(participant => TypeGraph.Implements(type, participant)));
        return ConstructorViolations(outsideSystems, static type =>
            QueryArguments(type).FirstOrDefault(IsWritingTerm) is { } term ? $"it writes through {Name(term)}; code outside systems reads the world, so take a ReadOnlySpan" : null);
    }

    // AddUnique registers at most ReadOnlyUnique<TReadOnly>, so the provider cannot give code outside systems a Unique<T>. A game that also registers the
    // unique's class as a service can, which this finds by the classes systems take as Unique<T>. A view taking Unique<T> is reported by
    // ViewsReadOnlyTheirViewModel, so only the class is checked for views.
    // Classes are matched exactly, not by assignability: the class implements its read-only type, so assignability would flag IReadOnlyTerrain too.
    // A writable interface of the class gets through only if the game registers the instance a second time under it.
    // A unique that no system takes as Unique<T> is not known here, because AddUnique calls are neither constructors nor types. Code outside
    // systems is the updatables and renderers, as in OutsideSystemsTakeNoWritingQueries; audio has no Pixely interface to find it by.
    public static IReadOnlyList<string> OutsideSystemsTakeNoWritableUniques(OrangeArchitectureOptions options)
    {
        HashSet<Type> writableUniques = Classes(options).Where(type => typeof(ISystem).IsAssignableFrom(type))
            .SelectMany(TypeGraph.Constructors).SelectMany(constructor => constructor.GetParameters())
            .SelectMany(parameter => TypeGraph.Expand(parameter.ParameterType)).Where(IsUnique)
            .Select(unique => unique.GetGenericArguments()[0]).ToHashSet();
        const string reason = "it writes a unique; code outside systems reads a unique through the ReadOnlyUnique of its read-only type";
        IEnumerable<Type> outsideSystems = Classes(options).Where(type => !typeof(ISystem).IsAssignableFrom(type) && OutsideSystemTypes.Any(participant => TypeGraph.Implements(type, participant)));
        IEnumerable<Type> views = Classes(options).Where(type => typeof(UiView).IsAssignableFrom(type));
        return [.. ConstructorViolations(outsideSystems, type => IsUnique(type) || writableUniques.Contains(type) ? reason : null),
            .. ConstructorViolations(views, type => writableUniques.Contains(type) ? reason : null)];

        static bool IsUnique(Type type) => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Unique<>);
    }

    // With two writers, Changed is raised twice every frame and the view shows only the later writer's values. A writer is a system, or an
    // updatable for a view model of state outside the world. A view model nothing writes passes, e.g. one whose writer is a plain service.
    // View models come from the declared classes, so a closed generic one such as ListViewModel<Item> is not counted, only its open definition,
    // which no constructor takes. Generic view models are rare enough to leave that gap.
    public static IReadOnlyList<string> ViewModelsHaveOneWriter(OrangeArchitectureOptions options)
    {
        Type[] writers = Classes(options).Where(type => typeof(ISystem).IsAssignableFrom(type) || typeof(IUpdatable).IsAssignableFrom(type)).ToArray();
        List<string> violations = new List<string>();
        foreach (Type viewModel in Classes(options).Where(type => !type.IsGenericTypeDefinition && typeof(IUiViewModel).IsAssignableFrom(type)))
        {
            Type[] viewModelWriters = writers.Where(writer => TypeGraph.Constructors(writer).SelectMany(constructor => constructor.GetParameters())
                .Any(parameter => parameter.ParameterType != typeof(object) && parameter.ParameterType.IsAssignableFrom(viewModel))).ToArray();
            if (viewModelWriters.Length > 1)
            {
                violations.Add($"{viewModel.FullName}: {viewModelWriters.Length} systems or updatables take it ({string.Join(", ", viewModelWriters.Select(writer => writer.Name))}); one of them writes each view model");
            }
        }

        return violations;
    }

    public static IReadOnlyList<string> VocabularyHoldsOnlyPrimitives(OrangeArchitectureOptions options)
    {
        return options.Assemblies.SelectMany(TypeGraph.DeclaredTypes).Where(type => OrangeArchitectureOptions.IsWithin(type.Namespace ?? string.Empty, options.VocabularyNamespace))
            .Where(type => !type.IsEnum && !TypeGraph.IsReadOnlyRecordStruct(type) && !IsConstants(type))
            .Select(type => $"{type.FullName}: {options.VocabularyNamespace} holds only enums, read-only record structs and static classes of constant values; move it to the namespace that names it")
            .ToArray();
    }

    // A static class with only const and static readonly fields, e.g. the limits several namespaces share. A static readonly array or list stays
    // writable to everyone, so a static readonly field holds a value type or a string.
    private static bool IsConstants(Type type)
    {
        return TypeGraph.IsStatic(type) && !TypeGraph.DeclaredMethods(type).Any() && !TypeGraph.DeclaredProperties(type).Any()
            && TypeGraph.DeclaredFields(type).All(field => field.IsLiteral || field.IsStatic && field.IsInitOnly && (field.FieldType.IsValueType || field.FieldType == typeof(string)));
    }

    // A ReadOnlySpan guards only the component's own bytes: a List<T> it holds stays writable to every reader. A string cannot change, so it passes.
    public static IReadOnlyList<string> ComponentsHoldNoReferences(OrangeArchitectureOptions options)
    {
        return ComponentTypes(options)
            .SelectMany(component => ReferenceFields(component, component.Name)
                .Select(field => $"{component.FullName}: {field.Path} holds a reference, {Name(field.Type)}, which code reading the component can change; hold values or ids"))
            .ToArray();
    }

    // Every struct a query, spawner, filter, lookup, adder or remover names, from the types the scanned assemblies declare and their signatures.
    // The base classes are walked too, so Masses : ComponentQuery<Mass> with ComponentQuery<T> : Query<ReadOnlySpan<T>> names Mass.
    private static IEnumerable<Type> ComponentTypes(OrangeArchitectureOptions options)
    {
        return options.Assemblies.SelectMany(TypeGraph.DeclaredTypes).SelectMany(type => TypeGraph.SignatureTypes(type).Concat(BaseTypes(type)))
            .Where(type => type.IsGenericType && type.Assembly == EcsAssembly)
            .SelectMany(type => type.GetGenericArguments()).Select(Unwrap)
            .Where(type => type.IsValueType && !type.IsByRefLike && !type.IsGenericParameter && type != typeof(Entity))
            .Distinct();
    }

    // A field of a struct type is followed into, so a struct inside a component holding a list is found too.
    private static IEnumerable<(string Path, Type Type)> ReferenceFields(Type type, string path)
    {
        foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            Type fieldType = field.FieldType;
            // An auto-property's backing field is named <Name>k__BackingField.
            string fieldPath = $"{path}.{(field.Name.StartsWith('<') ? field.Name[1..field.Name.IndexOf('>')] : field.Name)}";
            if (fieldType == typeof(string) || fieldType.IsPointer || fieldType.IsFunctionPointer)
            {
                continue;
            }

            if (!fieldType.IsValueType)
            {
                yield return (fieldPath, fieldType);
            }
            // A primitive's only field is itself, e.g. Int32.m_value.
            else if (!fieldType.IsPrimitive && fieldType != type)
            {
                foreach ((string Path, Type Type) nested in ReferenceFields(fieldType, fieldPath))
                {
                    yield return nested;
                }
            }
        }
    }

    // One violation per constructor parameter whose type, or a type it is built from such as the World of Func<World>, gives a reason.
    private static IReadOnlyList<string> ConstructorViolations(IEnumerable<Type> types, Func<Type, string?> reason)
    {
        return types.SelectMany(TypeGraph.Constructors)
            .SelectMany(constructor => constructor.GetParameters()
                .SelectMany(parameter => TypeGraph.Expand(parameter.ParameterType).Select(reason).OfType<string>().Take(1)
                    .Select(why => $"{TypeGraph.Describe(constructor.DeclaringType!)}: constructor takes {Name(parameter.ParameterType)} through {parameter.Name}; {why}")))
            .ToArray();
    }

    // World, queries, spawners, lookups, adders, removers and the destroyer are all classes of Pixely.Ecs or derive from one. Entity is a struct.
    private static bool IsWorldAccess(Type type)
    {
        return type.IsClass && BaseTypes(type).Any(candidate => candidate.Assembly == EcsAssembly);
    }

    private static bool IsMessageAccess(Type type)
    {
        return type.IsClass && type.Assembly == MessagesAssembly;
    }

    // The type arguments of the Query<...> a query class derives from.
    private static Type[] QueryArguments(Type type)
    {
        Type? query = BaseTypes(type).FirstOrDefault(candidate => candidate.IsGenericType && candidate.BaseType == typeof(Query));
        return query?.GetGenericArguments() ?? [];
    }

    private static bool IsWritingTerm(Type term)
    {
        return term.IsGenericType && (term.GetGenericTypeDefinition() == typeof(Span<>) || term.GetGenericTypeDefinition() == typeof(Optional<>) && IsWritingTerm(term.GetGenericArguments()[0]));
    }

    private static IEnumerable<Type> BaseTypes(Type type)
    {
        for (Type? current = type; current != null; current = current.BaseType)
        {
            yield return current;
        }
    }

    // ReadOnlyComponentLookup<Health> rather than ReadOnlyComponentLookup`1.
    private static string Name(Type type)
    {
        int tick = type.Name.IndexOf('`');
        return type.IsGenericType && tick >= 0 ? $"{type.Name[..tick]}<{string.Join(", ", type.GetGenericArguments().Select(Name))}>" : type.Name;
    }

    private static IEnumerable<Type> Classes(OrangeArchitectureOptions options)
    {
        return options.Assemblies.SelectMany(TypeGraph.DeclaredTypes).Where(type => type.IsClass && !type.IsAbstract);
    }
}
