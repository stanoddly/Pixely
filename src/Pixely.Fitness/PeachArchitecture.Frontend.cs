using System.Reflection;
using Pixely.Ui;

namespace Pixely.Fitness;

public static partial class PeachArchitecture
{
    // Frontend and the actors show nothing but their registrars; Executable composes them through those alone.
    private static IReadOnlyList<string> DirectorAndActorPublicSurfaceIsTheRegistrar(PeachArchitectureOptions options)
    {
        return options.ActorAssemblies.Append(options.Frontend)
            .SelectMany(assembly => TypeGraph.DeclaredTypes(assembly).Where(type => TypeGraph.IsPublicSurface(type) && !(TypeGraph.IsStatic(type) && type.Namespace == options.RootNamespaceOf(assembly))))
            .Select(type => type.FullName!)
            .ToArray();
    }

    // 35. Forms own every UiView; output owns none.
    private static IReadOnlyList<string> FormsOwnEveryUiView(PeachArchitectureOptions options)
    {
        IEnumerable<string> misplaced = TypeGraph.DeclaredTypes(options.Frontend)
            .Where(type => typeof(IUiView).IsAssignableFrom(type) && !type.IsAbstract && type.Namespace != options.FormsNamespace)
            .Select(type => type.FullName!);
        IEnumerable<string> output = options.OutputAssemblies.SelectMany(TypeGraph.DeclaredTypes)
            .Where(type => typeof(IUiView).IsAssignableFrom(type))
            .Select(type => type.FullName!);
        return misplaced.Concat(output).Order(StringComparer.Ordinal).ToArray();
    }

    // 36. A form syncs from its view model; it MUST NOT read State or invoke Mechanics directly. Reflection sees what a form names, not
    // what a method body calls, so a form that reaches State through a service it names elsewhere is caught by that name.
    private static IReadOnlyList<string> FormsNameNoStateOrMechanics(PeachArchitectureOptions options)
    {
        string[] forbidden = [options.StateNamespace, options.MechanicsNamespace];
        return TypeGraph.DeclaredTypes(options.Frontend)
            .Where(type => typeof(IUiView).IsAssignableFrom(type))
            .SelectMany(type => TypeGraph.SignatureTypes(type).Where(named => forbidden.Contains(named.Namespace)).Select(named => $"{type.FullName} -> {named.FullName}"))
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    // 39. Output presents what Frontend gives it; it names nothing from Pixely.Input.
    private static IReadOnlyList<string> OutputReadsNoInput(PeachArchitectureOptions options)
    {
        return options.OutputAssemblies
            .SelectMany(TypeGraph.DeclaredTypes)
            .SelectMany(type => TypeGraph.SignatureTypes(type).Where(named => named.Namespace?.StartsWith("Pixely.Input", StringComparison.Ordinal) == true)
                .Select(named => $"{type.FullName} -> {named.FullName}"))
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    // 40. View models direct; output owns none.
    private static IReadOnlyList<string> OutputOwnsNoViewModels(PeachArchitectureOptions options)
    {
        return options.OutputAssemblies.SelectMany(TypeGraph.DeclaredTypes).Where(type => typeof(IUiViewModel).IsAssignableFrom(type)).Select(type => type.FullName!).ToArray();
    }

    // 41. What output shows: its registrar, constants, item and report shapes, a camera, an items collection.
    private static IReadOnlyList<string> OutputPublicSurfaceIsRegistrarItemsAndCamera(PeachArchitectureOptions options)
    {
        return options.OutputAssemblies
            .SelectMany(assembly => TypeGraph.DeclaredTypes(assembly).Where(type => TypeGraph.IsPublicSurface(type) && !IsAllowed(type)))
            .Select(type => type.FullName!)
            .ToArray();

        static bool IsAllowed(Type type)
        {
            return type.IsEnum || type.IsValueType || TypeGraph.IsRecord(type) || TypeGraph.IsStatic(type)
                || type.Name.EndsWith("Camera", StringComparison.Ordinal) || type.Name.EndsWith("Items", StringComparison.Ordinal);
        }
    }

    // 41a. Audio's item and report records name Vocabulary types only.
    private static IReadOnlyList<string> AudioRecordsNameOnlyVocabulary(PeachArchitectureOptions options)
    {
        if (options.Audio == null)
        {
            return [];
        }

        return TypeGraph.DeclaredTypes(options.Audio)
            .Where(type => TypeGraph.IsPublicSurface(type) && (TypeGraph.IsRecord(type) || type.IsValueType && !type.IsEnum))
            .SelectMany(type => TypeGraph.PublicSignatureTypes(type)
                .Where(named => named.Assembly == options.Game && named.Namespace != options.VocabularyNamespace)
                .Select(named => $"{type.FullName} -> {named.FullName}"))
            .Distinct()
            .ToArray();
    }

    // 43. An actor owns no state that outlives a call: every field is readonly, and a collection it holds is a read-only one.
    private static IReadOnlyList<string> ActorsOwnNoState(PeachArchitectureOptions options)
    {
        return options.ActorAssemblies
            .SelectMany(TypeGraph.DeclaredTypes)
            .SelectMany(TypeGraph.DeclaredFields)
            .Where(field => !field.IsStatic && (!field.IsInitOnly || TypeGraph.IsEnumerable(field.FieldType) && !IsReadOnlyCollection(field.FieldType)))
            .Select(TypeGraph.Describe)
            .ToArray();
    }
}
