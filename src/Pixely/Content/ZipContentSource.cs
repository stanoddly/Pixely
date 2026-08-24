using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;

namespace Pixely.Content;

/// <summary>
/// A content file within a ZIP archive.
/// </summary>
internal sealed class ZipContentFile : ContentFile
{
    private readonly ZipArchiveEntry _entry;

    public ZipContentFile(ZipArchiveEntry entry)
    {
        _entry = entry;
    }

    public override string Path => _entry.FullName;

    public override Stream Open()
    {
        // Decompress the ZIP entry into a memory stream for full seeking capability
        MemoryStream memoryStream = new((int)_entry.Length);
        using (Stream entryStream = _entry.Open())
        {
            entryStream.CopyTo(memoryStream);
        }

        // Reset position to beginning and return the seekable memory stream
        memoryStream.Position = 0;
        return memoryStream;
    }
}

/// <summary>
/// A content source backed by a ZIP archive.
/// </summary>
public class ZipContentSource : ContentSource
{
    private readonly ZipArchive _archive;
    private readonly Dictionary<string, List<ZipContentFile>> _filesByDirectory;
    private readonly Dictionary<string, List<string>> _directoriesByParent;
    private bool _disposed;

    private ZipContentSource(ZipArchive archive)
    {
        _archive = archive;
        _filesByDirectory = new Dictionary<string, List<ZipContentFile>>();
        _directoriesByParent = new Dictionary<string, List<string>>();

        // Ensure root directory exists
        _directoriesByParent[""] = new List<string>();

        // Index all entries
        foreach (ZipArchiveEntry entry in _archive.Entries)
        {
            string normalizedPath = NormalizePath(entry.FullName);

            if (string.IsNullOrEmpty(entry.Name))
            {
                AddDirectoryToHierarchy(normalizedPath);
                continue;
            }

            string directory = GetDirectoryPath(normalizedPath);

            // Add file to its directory
            if (!_filesByDirectory.TryGetValue(directory, out List<ZipContentFile>? files))
            {
                files = new List<ZipContentFile>();
                _filesByDirectory[directory] = files;
            }

            files.Add(new ZipContentFile(entry));

            // Build directory hierarchy
            AddDirectoryToHierarchy(directory);
        }
    }

    private void AddDirectoryToHierarchy(string directory)
    {
        if (string.IsNullOrEmpty(directory))
        {
            return;
        }

        // Split path into components
        string[] parts = directory.Split('/');
        string currentPath = "";

        for (int i = 0; i < parts.Length; i++)
        {
            string parentPath = currentPath;

            // Build current path
            if (i > 0)
            {
                currentPath += "/";
            }

            currentPath += parts[i];

            // Add current directory to parent's children
            if (!_directoriesByParent.TryGetValue(parentPath, out List<string>? children))
            {
                children = new List<string>();
                _directoriesByParent[parentPath] = children;
            }

            if (!children.Contains(currentPath))
            {
                children.Add(currentPath);
            }
        }

        if (!_directoriesByParent.ContainsKey(directory))
        {
            _directoriesByParent[directory] = new List<string>();
        }
    }

    /// <summary>
    /// Creates a new ZipContentSource from the specified ZIP file path.
    /// </summary>
    /// <param name="zipPath">Path to the ZIP file</param>
    /// <returns>A new instance of ZipContentSource</returns>
    public static ZipContentSource Create(string zipPath)
    {
        if (string.IsNullOrEmpty(zipPath))
        {
            throw new ArgumentException("Path cannot be null or empty", nameof(zipPath));
        }

        if (!File.Exists(zipPath))
        {
            throw new FileNotFoundException("ZIP file not found", zipPath);
        }

        ZipArchive archive = System.IO.Compression.ZipFile.OpenRead(zipPath);
        return new ZipContentSource(archive);
    }

    public override bool TryGetFiles(ReadOnlySpan<char> path, out ReadOnlySpan<ContentFile> result)
    {
        ThrowIfDisposed();

        string normalizedPath = NormalizePath(path);

        if (_filesByDirectory.TryGetValue(normalizedPath, out List<ZipContentFile>? files))
        {
            ContentFile[] contentFiles = new ContentFile[files.Count];
            for (int i = 0; i < files.Count; i++)
            {
                contentFiles[i] = files[i];
            }
            result = contentFiles;
            return true;
        }

        result = Array.Empty<ContentFile>();
        return _directoriesByParent.ContainsKey(normalizedPath);
    }

    public override bool TryGetDirectories(ReadOnlySpan<char> path, out ReadOnlySpan<string> result)
    {
        ThrowIfDisposed();

        string normalizedPath = NormalizePath(path);

        if (_directoriesByParent.TryGetValue(normalizedPath, out List<string>? directories))
        {
            result = directories.ToArray();
            return true;
        }

        result = Array.Empty<string>();
        return false;
    }

    public override bool TryGetFile(ReadOnlySpan<char> path, [NotNullWhen(true)] out ContentFile? file)
    {
        ThrowIfDisposed();

        string normalizedPath = NormalizePath(path);

        // Look for the file directly in the archive
        ZipArchiveEntry? entry = _archive.GetEntry(normalizedPath);
        if (entry != null && !string.IsNullOrEmpty(entry.Name))
        {
            file = new ZipContentFile(entry);
            return true;
        }

        file = null;
        return false;
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _archive.Dispose();
                _filesByDirectory.Clear();
                _directoriesByParent.Clear();
            }

            _disposed = true;
        }
    }

    public override void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(ZipContentSource));
        }
    }

    private static string NormalizePath(ReadOnlySpan<char> path)
    {
        if (path.IsEmpty || path.SequenceEqual("."))
        {
            return string.Empty;
        }

        // Replace backslashes with forward slashes and trim leading/trailing slashes
        return path.ToString().Replace('\\', '/').Trim('/');
    }

    private static string GetDirectoryPath(string path)
    {
        int lastSlashIndex = path.LastIndexOf('/');
        if (lastSlashIndex < 0)
        {
            return string.Empty;
        }

        return path.Substring(0, lastSlashIndex);
    }
}
