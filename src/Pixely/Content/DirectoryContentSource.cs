using System.Diagnostics.CodeAnalysis;

namespace Pixely.Content;

public sealed class NativeContentFile : ContentFile
{
    private readonly string _filename;
    private readonly string _nativeFilename;

    public NativeContentFile(string filename, string nativeFilename)
    {
        _filename = filename;
        _nativeFilename = nativeFilename;
    }

    public override Stream Open()
    {
        return File.OpenRead(_nativeFilename);
    }

    public override string Path => _filename;
    public long Length => new FileInfo(_nativeFilename).Length;
}

public sealed class DirectoryContentSource : ContentSource
{
    public static readonly bool NativeDirSeparatorIsSlash = Path.DirectorySeparatorChar == '/';
    public string RootPath { get; }

    public DirectoryContentSource(string rootPath)
    {
        RootPath = Path.GetFullPath(rootPath);
    }

    private string FromVirtualToNativePath(string path)
    {
        string relativePath = path;
        if (!NativeDirSeparatorIsSlash)
        {
            relativePath = path.Replace(Path.DirectorySeparatorChar, '/');
        }

        string almostReadAbsolutePath = Path.Combine(RootPath, relativePath);
        // if there was a dot it may lead to something like: a/./b
        return Path.GetFullPath(almostReadAbsolutePath);

    }

    private string FromRelativeToVirtualPath(string path)
    {
        if (NativeDirSeparatorIsSlash)
        {
            return path;
        }

        return path.Replace(Path.DirectorySeparatorChar, '/');
    }

    private string FromAbsoluteToVirtualPath(string path)
    {
        string relativePath = Path.GetRelativePath(RootPath, path);

        if (NativeDirSeparatorIsSlash)
        {
            return relativePath;
        }

        return relativePath.Replace(Path.DirectorySeparatorChar, '/');
    }

    public override bool TryGetFiles(ReadOnlySpan<char> path, out ReadOnlySpan<ContentFile> result)
    {
        string nativePath = FromVirtualToNativePath(path.ToString());

        if (!Directory.Exists(nativePath))
        {
            result = Array.Empty<ContentFile>();
            return false;
        }

        string[] filenames = Directory.GetFiles(nativePath);
        ContentFile[] files = new ContentFile[filenames.Length];

        for (int i = 0; i < filenames.Length; i++)
        {
            string relativeFilename = Path.GetRelativePath(RootPath, filenames[i]);
            string virtualPath = FromRelativeToVirtualPath(relativeFilename);
            files[i] = new NativeContentFile(virtualPath, filenames[i]);
        }

        result = files;
        return true;
    }

    public override bool TryGetDirectories(ReadOnlySpan<char> path, out ReadOnlySpan<string> result)
    {
        string nativePath = FromVirtualToNativePath(path.ToString());

        if (!Directory.Exists(nativePath))
        {
            result = Array.Empty<string>();
            return false;
        }

        string[] filenames = Directory.GetDirectories(nativePath);
        string[] directories = new string[filenames.Length];

        for (int i = 0; i < filenames.Length; i++)
        {
            string relativeFilename = Path.GetRelativePath(RootPath, filenames[i]);
            directories[i] = FromRelativeToVirtualPath(relativeFilename);
        }

        result = directories;
        return true;
    }

    public override bool TryGetFile(ReadOnlySpan<char> path, [NotNullWhen(true)] out ContentFile? file)
    {
        string pathString = path.ToString();
        string nativePath = FromVirtualToNativePath(pathString);

        if (!File.Exists(nativePath))
        {
            file = null;
            return false;
        }

        file = new NativeContentFile(pathString, nativePath);
        return true;
    }
}
