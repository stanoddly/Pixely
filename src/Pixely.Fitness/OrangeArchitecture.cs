using System.Reflection;
using Pixely.Ecs;
using Pixely.Messages;
using Pixely.RenderOrchestration;
using Pixely.Ui;

namespace Pixely.Fitness;

/// <summary>
/// The fitness functions of the Orange architecture, see docs/orange-architecture.md. They check types and constructors only, because the
/// architecture has not settled a project layout.
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
            new FitnessResult("Orange ViewModelsHaveOneWriter", ViewModelsHaveOneWriter(options)),
            new FitnessResult("Orange VocabularyHoldsOnlyPrimitives", VocabularyHoldsOnlyPrimitives(options)),
            new FitnessResult("Orange ComponentsHoldNoReferences", ComponentsHoldNoReferences(options))
        ]);
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

    // AddQuery already throws for a writing query, but only when the provider is built. A helper a system builds and hands its query is not
    // checked, because it runs inside the system.
    public static IReadOnlyList<string> OutsideSystemsTakeNoWritingQueries(OrangeArchitectureOptions options)
    {
        IEnumerable<Type> outsideSystems = Classes(options).Where(type => !typeof(ISystem).IsAssignableFrom(type) && OutsideSystemTypes.Any(participant => TypeGraph.Implements(type, participant)));
        return ConstructorViolations(outsideSystems, static type =>
            QueryArguments(type).FirstOrDefault(IsWritingTerm) is { } term ? $"it writes through {Name(term)}; code outside systems reads the world, so take a ReadOnlySpan" : null);
    }

    // With two writers, Changed is raised twice every frame and the view shows only the later system's values. A view model no system writes
    // passes, because view models of state outside the world are not settled.
    // View models come from the declared classes, so a closed generic one such as ListViewModel<Item> is not counted, only its open definition,
    // which no constructor takes. Generic view models are rare enough to leave that gap.
    public static IReadOnlyList<string> ViewModelsHaveOneWriter(OrangeArchitectureOptions options)
    {
        Type[] systems = Classes(options).Where(type => typeof(ISystem).IsAssignableFrom(type)).ToArray();
        List<string> violations = new List<string>();
        foreach (Type viewModel in Classes(options).Where(type => !type.IsGenericTypeDefinition && typeof(IUiViewModel).IsAssignableFrom(type)))
        {
            Type[] writers = systems.Where(system => TypeGraph.Constructors(system).SelectMany(constructor => constructor.GetParameters())
                .Any(parameter => parameter.ParameterType != typeof(object) && parameter.ParameterType.IsAssignableFrom(viewModel))).ToArray();
            if (writers.Length > 1)
            {
                violations.Add($"{viewModel.FullName}: {writers.Length} systems take it ({string.Join(", ", writers.Select(writer => writer.Name))}); one system writes each view model");
            }
        }

        return violations;
    }

    public static IReadOnlyList<string> VocabularyHoldsOnlyPrimitives(OrangeArchitectureOptions options)
    {
        return options.Assemblies.SelectMany(assembly => TypeGraph.TypesIn(assembly, options.VocabularyNamespace))
            .Where(type => !type.IsVisible || !type.IsEnum && !TypeGraph.IsReadOnlyRecordStruct(type) && !IsConstants(type))
            .Select(type => $"{type.FullName}: {options.VocabularyNamespace} holds only public enums, read-only record structs and static classes of constant values; move it to the namespace that names it")
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

        static Type Unwrap(Type type)
        {
            bool wrapped = type.IsGenericType && (type.GetGenericTypeDefinition() == typeof(Span<>) || type.GetGenericTypeDefinition() == typeof(ReadOnlySpan<>) || type.GetGenericTypeDefinition() == typeof(Optional<>));
            return wrapped ? Unwrap(type.GetGenericArguments()[0]) : type;
        }
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
