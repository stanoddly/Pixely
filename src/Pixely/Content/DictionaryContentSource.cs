using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace Pixely.Content;

public class ByteContentFile : ContentFile
{
    private readonly byte[] _content;
    public override string Path { get; }

    public ByteContentFile(string path, byte[] content)
    {
        Path = path;
        _content = content;
    }

    public ByteContentFile(string path, ReadOnlySpan<byte> content)
    {
        Path = path;
        _content = content.ToArray();
    }

    public virtual long Length => _content.Length;
    public override Stream Open()
    {
        return new MemoryStream(_content);
    }
}

public class DictionaryContentSource : ContentSource
{
    private readonly FrozenDictionary<string, ImmutableArray<ContentFile>> _files;
    private readonly FrozenDictionary<string, ImmutableArray<string>> _directories;
    private readonly FrozenDictionary<string, ContentFile> _directFilesLookup;
    private readonly FrozenDictionary<string, ImmutableArray<ContentFile>>.AlternateLookup<ReadOnlySpan<char>> _filesLookup;
    private readonly FrozenDictionary<string, ImmutableArray<string>>.AlternateLookup<ReadOnlySpan<char>> _directoriesLookup;
    private readonly FrozenDictionary<string, ContentFile>.AlternateLookup<ReadOnlySpan<char>> _directFilesSpanLookup;

    public DictionaryContentSource(FrozenDictionary<string, ImmutableArray<ContentFile>> files,
        FrozenDictionary<string, ImmutableArray<string>> directories)
    {
        _files = files;
        _directories = directories;
        _directFilesLookup = _files.Values.SelectMany(item => item).ToFrozenDictionary(item => item.Path);
        _filesLookup = _files.GetAlternateLookup<ReadOnlySpan<char>>();
        _directoriesLookup = _directories.GetAlternateLookup<ReadOnlySpan<char>>();
        _directFilesSpanLookup = _directFilesLookup.GetAlternateLookup<ReadOnlySpan<char>>();
    }

    public override bool TryGetFiles(ReadOnlySpan<char> path, out ReadOnlySpan<ContentFile> result)
    {
        if (_filesLookup.TryGetValue(path, out ImmutableArray<ContentFile> files))
        {
            result = files.AsSpan();
            return true;
        }

        if (_directoriesLookup.ContainsKey(path))
        {
            result = Array.Empty<ContentFile>();
            return true;
        }

        result = Array.Empty<ContentFile>();
        return false;
    }

    public override bool TryGetDirectories(ReadOnlySpan<char> path, out ReadOnlySpan<string> result)
    {
        if (_directoriesLookup.TryGetValue(path, out ImmutableArray<string> directories))
        {
            result = directories.AsSpan();
            return true;
        }

        result = Array.Empty<string>();
        return false;
    }

    public override bool TryGetFile(ReadOnlySpan<char> path, [NotNullWhen(true)] out ContentFile? file)
    {
        return _directFilesSpanLookup.TryGetValue(path, out file);
    }

    public static readonly DictionaryContentSource Empty = new(
        FrozenDictionary<string, ImmutableArray<ContentFile>>.Empty,
        FrozenDictionary<string, ImmutableArray<string>>.Empty);
}
