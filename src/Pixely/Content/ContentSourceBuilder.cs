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
            AddProjectDirectory(contentDirectory);
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
