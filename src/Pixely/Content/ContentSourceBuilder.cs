namespace Pixely.Content;

public class ContentSourceBuilder
{
    private readonly List<ContentSource> _sources = new();
    private bool _cached = false;

    public ContentSourceBuilder AddDirectory(string directory)
    {
        AddSource(new DirectoryContentSource(directory));
        return this;
    }

    public ContentSourceBuilder AddSource(ContentSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        _sources.Add(source);
        return this;
    }

    public ContentSourceBuilder AddProjectDirectory(string? subdirectory = null)
    {
        string contentDirectory = ResolveContentDirectory(AppContext.BaseDirectory, subdirectory);

        AddDirectory(contentDirectory);

        return this;
    }

    public ContentSourceBuilder AddDirectoryPattern(string pattern)
    {
        string[] directories = Directory.GetDirectories(AppContext.BaseDirectory, pattern);
        Array.Sort(directories, StringComparer.Ordinal);

        foreach (string directory in directories)
        {
            AddDirectory(directory);
        }

        return this;
    }

    public ContentSourceBuilder AddDefaultContent(string contentDirectory = "Content")
    {
        ArgumentException.ThrowIfNullOrEmpty(contentDirectory);

        string appDirectory = Path.GetFullPath(AppContext.BaseDirectory);
        string archivePath = Path.Combine(appDirectory, $"{contentDirectory}.pk3");
        string directoryPath = Path.Combine(appDirectory, contentDirectory);
        bool archiveExists = File.Exists(archivePath);

        if (archiveExists)
        {
            AddZip(archivePath);
        }

        if (Directory.Exists(directoryPath))
        {
            AddDirectory(directoryPath);
        }
        else if (!archiveExists)
        {
#if BROWSER
            // A browser app has no project tree to search; its content reaches the file system only as a PixelyBrowserVfsFile.
            throw new InvalidOperationException(
                $"Content not found. Checked '{archivePath}' and '{directoryPath}'. A browser app ships its content as a PixelyBrowserVfsFile with TargetPath {Path.GetRelativePath(appDirectory, archivePath)}.");
#else
            AddProjectDirectory(contentDirectory);
#endif
        }

        return this;
    }

    private static string ResolveContentDirectory(string baseDirectory, string? subdirectory)
    {
        string appDirectory = Path.GetFullPath(baseDirectory);
        string appContentDirectory = subdirectory != null
            ? Path.Combine(appDirectory, subdirectory)
            : appDirectory;

        if (Directory.Exists(appContentDirectory))
        {
            return appContentDirectory;
        }

#if BROWSER
        throw new InvalidOperationException(
            $"Content directory not found. Checked '{appContentDirectory}'. A browser app has no project directory; its files reach the file system as PixelyBrowserVfsFile items.");
#else
        DirectoryInfo? directory = new DirectoryInfo(appDirectory);

        while (directory != null)
        {
            if (directory.GetFiles("*.csproj").Length > 0)
            {
                string projectContentDirectory = subdirectory != null
                    ? Path.Combine(directory.FullName, subdirectory)
                    : directory.FullName;

                if (Directory.Exists(projectContentDirectory))
                {
                    return projectContentDirectory;
                }

                throw new InvalidOperationException(
                    $"Content directory not found. Checked '{appContentDirectory}' and '{projectContentDirectory}'.");
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"Content directory not found. Checked '{appContentDirectory}' and no project directory was found.");
#endif
    }

    public ContentSourceBuilder AddZip(string filename)
    {
        ZipContentSource source = ZipContentSource.Create(filename);
        AddSource(source);

        return this;
    }

    public ContentSourceBuilder AddZipPattern(string pattern)
    {
        string[] filenames = Directory.GetFiles(AppContext.BaseDirectory, pattern);
        foreach (string filename in filenames)
        {
            AddZip(filename);
        }

        return this;
    }

    public ContentSourceBuilder WithCache()
    {
        _cached = true;
        return this;
    }

    public ContentSource Create()
    {
        ContentSource finalContentSource;

        if (_sources.Count == 0)
        {
            return DictionaryContentSource.Empty;
        }

        if (_sources.Count == 1)
        {
            finalContentSource = _sources[0];
        }
        else
        {
            finalContentSource = new CompositeContentSource(_sources);
        }

        if (_cached)
        {
            finalContentSource = CachedContentSource.Create(finalContentSource);
        }

        return finalContentSource;
    }
}
