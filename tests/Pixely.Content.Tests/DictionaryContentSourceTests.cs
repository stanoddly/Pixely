using System.Collections.Frozen;
using System.Collections.Immutable;

namespace Pixely.Content.Tests;

public class DictionaryContentSourceTests : BaseContentSourceTests
{
    [SetUp]
    public void Setup()
    {
        FrozenDictionary<string, ImmutableArray<ContentFile>> files = new Dictionary<string, ImmutableArray<ContentFile>>
        {
            ["."] = [new ByteContentFile("a.txt", "Hello a"u8), new ByteContentFile("b.txt", "Hello b"u8)],
            ["dir1"] = [new ByteContentFile("dir1/dir1a.txt", "Hello dir1a"u8), new ByteContentFile("dir1/dir1b.txt", "Hello dir1b"u8)],
            ["dir2"] = [new ByteContentFile("dir2/dir2a.txt", "Hello dir2a"u8), new ByteContentFile("dir2/dir2b.txt", "Hello dir2b"u8)],
        }.ToFrozenDictionary();

        FrozenDictionary<string, ImmutableArray<string>> directories = new Dictionary<string, ImmutableArray<string>>
        {
            ["."] = ["dir1", "dir2"],
            ["dir1"] = [],
            ["dir2"] = []
        }.ToFrozenDictionary();

        Source = new DictionaryContentSource(files, directories);
    }
}
