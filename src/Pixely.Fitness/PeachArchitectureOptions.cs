using System.Reflection;
using System.Runtime.CompilerServices;

namespace Pixely.Fitness;

/// <summary>
/// What a game tells the generic rules: its prefix, the namespaces it adds beyond the document's,
/// whether it switches the hardened SHOULD off, and optionally its repository root. Everything else is
/// derived from the prefix, see <see cref="PeachGame"/>.
/// </summary>
public sealed record PeachArchitectureOptions(string GamePrefix)
{
    // Keyed by instance rather than held in a field, so a copy made with `with` resolves again and equality ignores the cache.
    private static readonly ConditionalWeakTable<PeachArchitectureOptions, PeachGame> ResolvedGames = new ConditionalWeakTable<PeachArchitectureOptions, PeachGame>();

    public IReadOnlyList<ExtraNamespace> ExtraGameNamespaces { get; init; } = [];

    // The directory holding src/Foo.Game/Foo.Game.csproj. When null, it is found by walking up from the test output directory.
    public string? RepositoryRoot { get; init; }

    // The hardened SHOULD a game may switch off.
    public bool MechanicsTakeStateRootThroughConstructor { get; init; } = true;

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

    internal PeachGame Resolved => ResolvedGames.GetValue(this, PeachGame.Resolve);

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
