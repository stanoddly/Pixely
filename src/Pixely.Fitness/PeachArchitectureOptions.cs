using System.Reflection;
using Pixely.App;
using Pixely.DependencyInjection;

namespace Pixely.Fitness;

/// <summary>
/// What a game tells the generic rules: which assemblies play which part, its state root, the
/// namespaces it adds beyond the document's, and how its two containers are composed. Optional
/// projects are null when the game has none.
/// </summary>
public sealed record PeachArchitectureOptions(
    string GamePrefix,
    Assembly Game,
    Assembly Frontend,
    Assembly? Rendering,
    Assembly? Audio,
    Assembly? Ai,
    Assembly? Scenario,
    Assembly Executable,
    Type StateRoot,
    IReadOnlyList<string> ExtraGameNamespaces,
    Action<PixelyAppBuilder> ComposeRoot,
    Action<ServiceCollection> ComposeStage)
{
    // Where the project files are, for the one function that reads them; null skips it, e.g. when the tests run from a package.
    public string? RepositoryRoot { get; init; }

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

    internal IEnumerable<Assembly> OutputAssemblies => new[] { Rendering, Audio }.Where(assembly => assembly != null)!;
    internal IEnumerable<Assembly> ActorAssemblies => new[] { Ai, Scenario }.Where(assembly => assembly != null)!;
    internal IEnumerable<Assembly> ProductionAssemblies => new[] { Game, Frontend, Rendering, Audio, Ai, Scenario, Executable }.Where(assembly => assembly != null)!;

    internal string RootNamespaceOf(Assembly assembly)
    {
        if (assembly == Game)
        {
            return GameNamespace;
        }

        if (assembly == Frontend)
        {
            return FrontendNamespace;
        }

        if (assembly == Rendering)
        {
            return RenderingNamespace;
        }

        if (assembly == Audio)
        {
            return AudioNamespace;
        }

        if (assembly == Ai)
        {
            return AiNamespace;
        }

        if (assembly == Scenario)
        {
            return ScenarioNamespace;
        }

        return ExecutableNamespace;
    }
}
