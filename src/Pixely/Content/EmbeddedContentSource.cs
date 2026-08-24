using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Reflection;

namespace Pixely.Content;

public sealed class EmbeddedContentFile : ContentFile
{
    private readonly Assembly _assembly;
    public EmbeddedContentFile(Assembly assembly, string resourceName)
    {
        Path = resourceName;
        _assembly = assembly;
    }
    public override string Path { get; }

    public override Stream Open()
    {
        Stream? stream = _assembly.GetManifestResourceStream(Path);

        if (stream == null)
        {
            throw new FileNotFoundException($"Embedded resource not found: {Path}");
        }

        return stream;
    }
}

public static class EmbeddedContentSource
{
    public static ContentSource Create(Assembly assembly)
    {
        Dictionary<string, List<string>> directoryToDirectoriesLookup = new();
        Dictionary<string, List<ContentFile>> directoryToFilesLookup = new();

        foreach (string resourceName in assembly.GetManifestResourceNames())
        {
            string? directory = VirtualPath.GetDirectoryName(resourceName);

            string parentDirectory = directory ?? string.Empty;

            List<ContentFile> files = directoryToFilesLookup.GetValueOrNew(parentDirectory);
            files.Add(new EmbeddedContentFile(assembly, resourceName));

            if (directory == null)
            {
                continue;
            }

            while (directory != null)
            {
                string previous = directory;
                directory = VirtualPath.GetDirectoryName(previous);
                parentDirectory = directory ?? string.Empty;

                List<string> directories = directoryToDirectoriesLookup.GetValueOrNew(parentDirectory);

                directories.Add(previous);
            }
        }

        FrozenDictionary<string, ImmutableArray<string>> frozenDirectories = directoryToDirectoriesLookup
            .Select(pair => new KeyValuePair<string, ImmutableArray<string>>(pair.Key, pair.Value.ToImmutableArray())).ToFrozenDictionary();
        FrozenDictionary<string, ImmutableArray<ContentFile>> frozenFiles = directoryToFilesLookup
            .Select(pair => new KeyValuePair<string, ImmutableArray<ContentFile>>(pair.Key, pair.Value.ToImmutableArray())).ToFrozenDictionary();

        return new DictionaryContentSource(frozenFiles, frozenDirectories);
    }
}
