using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace Pixely.Content;

public sealed class CachedContentSource : ContentSource
{
    private readonly ContentSource _source;
    private readonly DictionaryContentSource _cache;

    private CachedContentSource(ContentSource source, DictionaryContentSource cache)
    {
        _source = source;
        _cache = cache;
    }

    public override bool TryGetFiles(ReadOnlySpan<char> path, out ReadOnlySpan<ContentFile> result)
    {
        return _cache.TryGetFiles(path, out result);
    }

    public override bool TryGetDirectories(ReadOnlySpan<char> path, out ReadOnlySpan<string> result)
    {
        return _cache.TryGetDirectories(path, out result);
    }

    public override bool TryGetFile(ReadOnlySpan<char> path, [NotNullWhen(true)] out ContentFile? file)
    {
        return _cache.TryGetFile(path, out file);
    }

    public override void Dispose()
    {
        _source.Dispose();
    }

    public static ContentSource Create(ContentSource source)
    {
        Stack<string> analyzedDirectories = new();
        analyzedDirectories.Push(".");

        List<(string, ImmutableArray<string>)> resultDirectories = new();
        List<(string, ImmutableArray<ContentFile>)> resultFiles = new();

        while (analyzedDirectories.TryPop(out string? directory))
        {
            ReadOnlySpan<string> sourceSubdirectories = source.GetDirectories(directory);
            resultDirectories.Add((directory, ImmutableArray.Create(sourceSubdirectories)));

            ReadOnlySpan<ContentFile> sourceFiles = source.GetFiles(directory);

            resultFiles.Add((directory, ImmutableArray.Create(sourceFiles)));

            foreach (string sourceSubdirectory in sourceSubdirectories)
            {
                analyzedDirectories.Push(sourceSubdirectory);
            }
        }

        FrozenDictionary<string, ImmutableArray<string>> frozenDirectories =
            resultDirectories.ToFrozenDictionary(item => item.Item1, item => item.Item2);
        FrozenDictionary<string, ImmutableArray<ContentFile>> frozenFiles =
            resultFiles.ToFrozenDictionary(item => item.Item1, item => item.Item2);

        return new CachedContentSource(source, new DictionaryContentSource(frozenFiles, frozenDirectories));
    }
}
