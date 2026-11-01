using System.Reflection;
using Pixely.Ui;

namespace Pixely.Fitness;

public static partial class PeachArchitecture
{
    // Only Executable references Frontend and the actors, so they expose nothing public but their registrars.
    private static IReadOnlyList<string> FrontendAndActorPublicSurfaceIsTheRegistrar(PeachArchitectureOptions options)
    {
        return options.ActorAssemblies.Append(options.Frontend)
            .SelectMany(assembly => TypeGraph.DeclaredTypes(assembly).Where(type => TypeGraph.IsPublicSurface(type) && !(TypeGraph.IsStatic(type) && type.Namespace == options.RootNamespaceOf(assembly))))
            .Select(type => type.FullName!)
            .ToArray();
    }

    // Forms own every UiView; an output MUST NOT own a form.
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

    // A form syncs from its view model; it MUST NOT read State or invoke Mechanics directly. Reflection sees what a form names, not
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

    // An output MUST NOT read input, so it names nothing from Pixely.Input.
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

    // An output MUST NOT own a view model.
    private static IReadOnlyList<string> OutputOwnsNoViewModels(PeachArchitectureOptions options)
    {
        return options.OutputAssemblies.SelectMany(TypeGraph.DeclaredTypes).Where(type => typeof(IUiViewModel).IsAssignableFrom(type)).Select(type => type.FullName!).ToArray();
    }

    // An actor owns no state that outlives a call: every field is readonly, and a collection it holds is a read-only one.
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
