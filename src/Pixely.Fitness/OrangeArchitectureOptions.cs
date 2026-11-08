using System.Reflection;

namespace Pixely.Fitness;

/// <summary>
/// The game's name, which names its namespaces, the assemblies the Orange rules are checked in, and the namespaces the game adds beyond the
/// document's.
/// </summary>
public sealed record OrangeArchitectureOptions(string GameName, IReadOnlyList<Assembly> Assemblies)
{
    // The namespaces orange-architecture.md names besides the root, each of which may have sub-namespaces.
    private static readonly string[] NamedNamespaceNames = ["Vocabulary", "Components", "Messages", "WorldAccess", "Forms", "Systems", "Rules", "Rendering", "Audio", "Saving"];

    public OrangeArchitectureOptions(string gameName, params Assembly[] assemblies) : this(gameName, (IReadOnlyList<Assembly>)assemblies)
    {
    }

    public IReadOnlyList<ExtraNamespace> ExtraNamespaces { get; init; } = [];

    internal string VocabularyNamespace => $"{GameName}.Vocabulary";

    internal bool IsNamed(string ns)
    {
        return ns == GameName || NamedNamespaceNames.Any(name => IsWithin(ns, $"{GameName}.{name}"));
    }

    // The namespace itself or one of its sub-namespaces.
    internal static bool IsWithin(string ns, string outer)
    {
        return ns == outer || ns.StartsWith($"{outer}.", StringComparison.Ordinal);
    }
}
