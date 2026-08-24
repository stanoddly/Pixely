namespace Pixely.Content;

public class FileSystemBuilder
{
    private readonly List<VirtualFileSystem> _fileSystems = new();
    private bool _cached = false;

    internal IReadOnlyList<VirtualFileSystem> FileSystems => _fileSystems;

    public FileSystemBuilder AddContentFromDirectory(string directory)
    {
        AddSourceFileSystem(new NativeFileSystem(directory));
        return this;
    }

    public FileSystemBuilder AddSourceFileSystem(VirtualFileSystem virtualFileSystem)
    {
        _fileSystems.Add(virtualFileSystem);
        return this;
    }

    public FileSystemBuilder AddContentFromProjectDirectory(string? subdirectory = null)
    {
        string contentDirectory = ResolveContentDirectory(AppContext.BaseDirectory, subdirectory);

        AddContentFromDirectory(contentDirectory);

        return this;
    }

    public FileSystemBuilder AddContentFromDirectoryPattern(string pattern)
    {
        string[] directories = Directory.GetDirectories(AppContext.BaseDirectory, pattern);
        Array.Sort(directories, StringComparer.Ordinal);

        foreach (string directory in directories)
        {
            AddContentFromDirectory(directory);
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

    public FileSystemBuilder AddContentFromZip(string filename)
    {
        ZipFileSystem fileSystem = ZipFileSystem.Create(filename); 
        AddSourceFileSystem(fileSystem);
        
        return this;
    }

    public FileSystemBuilder AddContentFromZipPattern(string pattern)
    {
        string[] filenames = Directory.GetFiles(AppContext.BaseDirectory, pattern);
        foreach (string filename in filenames)
        {
            AddContentFromZip(filename);
        }

        return this;
    }

    public FileSystemBuilder WithCache()
    {
        _cached = true;
        return this;
    }

    public VirtualFileSystem Create()
    {
        VirtualFileSystem finalVirtualFileSystem;

        if (_fileSystems.Count == 0)
        {
            return DictFileSystem.Empty;
        }

        if (_fileSystems.Count == 1)
        {
            finalVirtualFileSystem = _fileSystems[0];
        }
        else
        {
            finalVirtualFileSystem = new CompositeFileSystem(_fileSystems);
        }

        if (_cached)
        {
            finalVirtualFileSystem = CachedFileSystem.Create(finalVirtualFileSystem);
        }

        return finalVirtualFileSystem;
    }
}
