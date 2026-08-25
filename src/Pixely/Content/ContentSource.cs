using System.Diagnostics.CodeAnalysis;

namespace Pixely.Content;

public abstract class ContentFile
{
    public abstract string Path { get; }
    public abstract Stream Open();
}

public abstract class ContentSource : IDisposable
{
    public abstract bool TryGetFiles(ReadOnlySpan<char> path, out ReadOnlySpan<ContentFile> result);
    public abstract bool TryGetDirectories(ReadOnlySpan<char> path, out ReadOnlySpan<string> result);
    public abstract bool TryGetFile(ReadOnlySpan<char> path, [NotNullWhen(true)] out ContentFile? file);

    public ReadOnlySpan<ContentFile> GetFiles(ReadOnlySpan<char> path)
    {
        if (TryGetFiles(path, out ReadOnlySpan<ContentFile> files))
        {
            return files;
        }

        throw new DirectoryNotFoundException(path.ToString());
    }

    public ReadOnlySpan<string> GetDirectories(ReadOnlySpan<char> path)
    {
        if (TryGetDirectories(path, out ReadOnlySpan<string> directories))
        {
            return directories;
        }

        throw new DirectoryNotFoundException(path.ToString());
    }

    public ContentFile GetFile(ReadOnlySpan<char> path)
    {
        if (TryGetFile(path, out ContentFile? contentFile))
        {
            return contentFile;
        }

        throw new FileNotFoundException(path.ToString());
    }

    public Stream OpenStream(ReadOnlySpan<char> path)
    {
        return GetFile(path).Open();
    }

    // TODO: dispose pattern https://learn.microsoft.com/en-us/dotnet/standard/design-guidelines/dispose-pattern
    public virtual void Dispose()
    {
    }
}
