using System.Reflection;

namespace Pixely.Fitness;

/// <summary>
/// A namespace a game adds to Foo.Game beyond the ones the document names, and why the document's
/// namespaces did not do.
/// </summary>
public sealed record ExtraNamespace(string Namespace, string Justification);

/// <summary>
/// What a game tells the generic rules: its prefix, the namespaces it adds beyond the document's, and
/// which hardened SHOULDs it switches off. Everything else is derived from the prefix, see
/// <see cref="PeachGame"/>.
/// </summary>
public sealed record PeachArchitectureOptions(string GamePrefix)
{
    private PeachGame? _game;

    public IReadOnlyList<ExtraNamespace> ExtraGameNamespaces { get; init; } = [];

    // Hardened SHOULDs a game may switch off.
    public bool MechanicsHaveNoPublicConstructors { get; init; } = true;
    public bool MechanicsTakeStateRootThroughConstructor { get; init; } = true;
    public bool GameGrantsNoInternalAccess { get; init; } = true;

    internal string GameNamespace => $"{GamePrefix}.Game";
    internal string VocabularyNamespace => $"{GameNamespace}.Vocabulary";
    internal string StateNamespace => $"{GameNamespace}.State";
    internal string MechanicsNamespace => $"{GameNamespace}.Mechanics";
    internal string SystemsNamespace => $"{GameNamespace}.Systems";
    internal string ObservationsNamespace => $"{GameNamespace}.Observations";
    internal string FrontendNamespace => $"{GamePrefix}.Frontend";
    internal string FormsNamespace => $"{FrontendNamespace}.Forms";
    internal string RenderingNamespace => $"{FrontendNamespace}.Rendering";
    internal string AudioNamespace => $"{FrontendNamespace}.Audio";
    internal string AiNamespace => $"{GamePrefix}.Ai";
    internal string ScenarioNamespace => $"{GamePrefix}.Scenario";
    internal string ExecutableNamespace => $"{GamePrefix}.Executable";

    internal IEnumerable<string> ExtraGameNamespaceNames => ExtraGameNamespaces.Select(extra => extra.Namespace);

    internal PeachGame Resolved => _game ??= PeachGame.Resolve(this);

    internal Assembly Game => Resolved.Game!;
    internal Assembly Frontend => Resolved.Frontend!;
    internal Assembly? Rendering => Resolved.Rendering;
    internal Assembly? Audio => Resolved.Audio;
    internal Assembly? Ai => Resolved.Ai;
    internal Assembly? Scenario => Resolved.Scenario;
    internal Assembly Executable => Resolved.Executable!;
    internal Type? StateRoot => Resolved.StateRoot;

    internal IEnumerable<Assembly> OutputAssemblies => new[] { Rendering, Audio }.Where(assembly => assembly != null)!;
    internal IEnumerable<Assembly> ActorAssemblies => new[] { Ai, Scenario }.Where(assembly => assembly != null)!;
    internal IEnumerable<Assembly> ProductionAssemblies => new[] { Game, Frontend, Rendering, Audio, Ai, Scenario, Executable }.Where(assembly => assembly != null)!;

    internal string RootNamespaceOf(Assembly assembly)
    {
        return Resolved.RootNamespaceOf(this, assembly);
    }
}
