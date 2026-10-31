using System.Reflection;

namespace Pixely.Fitness;

/// <summary>
/// The game's name, which names its Foo.Vocabulary namespace, and the assemblies the Orange rules are checked in.
/// </summary>
public sealed record OrangeArchitectureOptions(string GameName, IReadOnlyList<Assembly> Assemblies)
{
    public OrangeArchitectureOptions(string gameName, params Assembly[] assemblies) : this(gameName, (IReadOnlyList<Assembly>)assemblies)
    {
    }

    internal string VocabularyNamespace => $"{GameName}.Vocabulary";
}
