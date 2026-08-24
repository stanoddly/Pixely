using System.Diagnostics.CodeAnalysis;

namespace Pixely.Content;

public sealed class CompositeContentSource : ContentSource
{
    private readonly List<ContentSource> _sources;

    public CompositeContentSource(IEnumerable<ContentSource> sources)
    {
        _sources = sources.ToList();
    }

    public override bool TryGetFiles(ReadOnlySpan<char> path, out ReadOnlySpan<ContentFile> result)
    {
        Dictionary<string, ContentFile> files = new();
        bool foundFiles = false;

        foreach (ContentSource source in _sources)
        {
            bool found = source.TryGetFiles(path, out ReadOnlySpan<ContentFile> sourceFiles);

            if (!found)
            {
                continue;
            }

            foundFiles = true;

            foreach (ContentFile sourceFile in sourceFiles)
            {
                files[sourceFile.Path] = sourceFile;
            }
        }

        if (!foundFiles)
        {
            result = Array.Empty<ContentFile>();
            return false;
        }

        result = files.Values.ToArray();
        return true;
    }

    public override bool TryGetDirectories(ReadOnlySpan<char> path, out ReadOnlySpan<string> result)
    {
        HashSet<string>? finalDirectories = null;

        foreach (ContentSource source in _sources)
        {
            bool found = source.TryGetDirectories(path, out ReadOnlySpan<string> directories);

            if (found)
            {
                finalDirectories ??= new();
                foreach (string directory in directories)
                {
                    finalDirectories.Add(directory);
                }
            }
        }

        if (finalDirectories == null)
        {
            result = Array.Empty<string>();
            return false;
        }

        result = finalDirectories.ToArray();
        return true;
    }

    public override bool TryGetFile(ReadOnlySpan<char> path, [NotNullWhen(true)] out ContentFile? file)
    {
        for (int i = _sources.Count - 1; i >= 0; i--)
        {
            if (_sources[i].TryGetFile(path, out file))
            {
                return true;
            }
        }

        file = null;
        return false;
    }

    public override void Dispose()
    {
        List<Exception> exceptions = new();

        foreach (ContentSource source in _sources)
        {
            try
            {
                source.Dispose();
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        }

        if (exceptions.Any())
        {
            throw new AggregateException("Failed to dispose one or more content sources", exceptions);
        }
    }
}
